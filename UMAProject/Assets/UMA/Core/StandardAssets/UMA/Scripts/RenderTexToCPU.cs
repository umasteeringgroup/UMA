using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UMA;
using static UMA.UMAData;
using UnityEngine.Rendering;
using System.Collections.Concurrent;

namespace UMA
{
    public class RenderTexToCPU
    {
        public static Dictionary<int, RenderTexToCPU> renderTexturesToCPU = new Dictionary<int, RenderTexToCPU>();
        public static Queue<RenderTexToCPU> QueuedCopies = new Queue<RenderTexToCPU>();

        public RenderTexture texture;
        public GeneratedMaterial generatedMaterial;
        public string textureName;
        public int textureIndex;
        public Texture2D newTexture;
        public static int copiesEnqueued = 0;
        public static int copiesDequeued = 0;
        public static int unableToQueue = 0;
        public static int misseduploads = 0;
        public static int errorUploads = 0;
        public static int texturesUploaded = 0;

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
                QueueCopy(asyncAction);
            });
        }


        private void QueueCopy(AsyncGPUReadbackRequest asyncAction)
        {
            int instanceID = texture.GetInstanceID();
            if (renderTexturesToCPU.ContainsKey(instanceID))
            {
                renderTexturesToCPU.Remove(instanceID);
            }

            // if it's still valid, then create the texture and enqueue the apply method
            if (generatedMaterial != null && generatedMaterial.material != null)
            {
                newTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, texture.mipmapCount > 0, true);
                newTexture.SetPixelData(asyncAction.GetData<byte>(), 0);
#if UNITY_EDITOR
                // We can't count on the callback due to the fact that the editor may not be playing.
                // therefor, just run the apply during editing directly.
                if (!Application.isPlaying)
                {
                    ApplyTexture();
                    return;
                }
#endif
                copiesEnqueued++;
                QueuedCopies.Enqueue(this);
            }
            else
            {
                unableToQueue++;
            }
        }

        public static int PendingCopies()
        {
            return QueuedCopies.Count;
        }

        public static void ApplyQueuedCopies(int number)
        {
            if (number <= 0)
            {
                number = QueuedCopies.Count;
            }
            while (QueuedCopies.Count > 0)
            {
                copiesDequeued++;
                RenderTexToCPU copy = QueuedCopies.Dequeue();
                copy.ApplyTexture();
                number--;
                if (number <= 0)
                {
                    break;
                }
            }
        }

        private void ApplyTexture()
        {
            if (generatedMaterial != null && generatedMaterial.material != null)
            {
                try
                {

                    newTexture.Apply();
                    generatedMaterial.material.SetTexture(textureName, newTexture);
                    generatedMaterial.resultingAtlasList[textureIndex] = newTexture;
                    RenderTexture.ReleaseTemporary(texture);
                    texturesUploaded++;
                }
                catch (System.Exception e)
                {
                    errorUploads++;
                }
            }
            else
            {
                misseduploads++;
                // we made it to the application, but the material has since died.
                // we need to clean up the texture
                UMAUtils.DestroySceneObject(newTexture);
            }
        }
    }
}
