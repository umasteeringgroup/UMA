using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UMA;
using static UMA.UMAData;
using UnityEngine.Rendering;

namespace UMA
{
    public class RenderTexToCPU
    {
        public static Dictionary<int, RenderTexToCPU> renderTexturesToCPU = new Dictionary<int, RenderTexToCPU>();
        public RenderTexture texture;
        public GeneratedMaterial generatedMaterial;
        public string textureName;
        public int textureIndex;

        public RenderTexToCPU(RenderTexture texture, GeneratedMaterial generatedMaterial, string textureName, int textureIndex)
        {
            this.texture = texture;
            this.generatedMaterial = generatedMaterial;
            this.textureName = textureName;
            this.textureIndex = textureIndex;
            renderTexturesToCPU.Add(texture.GetInstanceID(), this);
        }

        public void DoAsyncCopy()
        {
            //Asynchronously
            AsyncGPUReadback.Request(texture, 0, (AsyncGPUReadbackRequest asyncAction) =>
            {
                int instanceID = texture.GetInstanceID();
                if (renderTexturesToCPU.ContainsKey(instanceID))
                {
                    renderTexturesToCPU.Remove(instanceID);
                }
                if (generatedMaterial != null && generatedMaterial.material != null)
                {
                    try
                    {
                        Texture previousTexture = generatedMaterial.resultingAtlasList[textureIndex];
                        Texture2D texture2D = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, texture.mipmapCount > 0, true);
                        texture2D.SetPixelData(asyncAction.GetData<byte>(), 0);
                        texture2D.Apply();
                        generatedMaterial.material.SetTexture(textureName, texture2D);
                        generatedMaterial.resultingAtlasList[textureIndex] = texture2D;
                        RenderTexture.ReleaseTemporary(texture);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("Error copying texture to CPU: " + e.Message);
                    }
                }
                else
                {
                }
            });
        }
    }
}
