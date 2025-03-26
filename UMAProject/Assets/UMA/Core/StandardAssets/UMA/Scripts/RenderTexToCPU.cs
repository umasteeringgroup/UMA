using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UMA;
using static UMA.UMAData;
using UnityEngine.Rendering;
using System.Collections.Concurrent;
using System.Linq;
using UnityEngine.Experimental.Rendering;

namespace UMA
{
    public class RenderTexToCPU
    {
        public static bool ApplyInline;
        public static Dictionary<int, RenderTexToCPU> renderTexturesToCPU = new Dictionary<int, RenderTexToCPU>();
        public static Queue<RenderTexToCPU> QueuedCopies = new Queue<RenderTexToCPU>();
        public static Dictionary<int, RenderTexture> renderTexturesToFree = new Dictionary<int, RenderTexture>();

        public RenderTexture texture;
        public GeneratedMaterial generatedMaterial;
        public string textureName;
        public int textureIndex;
        public Texture2D newTexture;
        public bool recreateMips;
        public static int copiesEnqueued = 0;
        public static int copiesDequeued = 0;
        public static int unableToQueue = 0;
        public static int misseduploads = 0;
        public static int errorUploads = 0;
        public static int texturesUploaded = 0;
        public static int renderTexturesCleanedUMAData = 0;
        public static int renderTexturesCleanedApplied = 0;
        public static int renderTexturesCleanedMissed = 0;

        public RenderTexToCPU(RenderTexture texture, GeneratedMaterial generatedMaterial, string textureName, int textureIndex, UMAGeneratorBase basegen)
        {
            this.texture = texture;
            this.generatedMaterial = generatedMaterial;
            this.textureName = textureName;
            this.textureIndex = textureIndex;
            this.recreateMips = basegen.convertMipMaps;
            renderTexturesToCPU.Add(texture.GetInstanceID(), this);
        }

        public void DoAsyncCopy()
        {
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
                var w = asyncAction.width;
                var h = asyncAction.height;

                if (w != texture.width || h != texture.height)
                {
                    // the texture has changed since we started the copy, so we can't use it.
                    // we need to clean up the texture
                    Debug.LogWarning("Texture size changed during copy, discarding copy. RenderTexture will remain in VRAM");
                    return;
                }

                GraphicsFormat gf = GraphicsFormatUtility.GetGraphicsFormat(texture.format,false);
                TextureFormat tf = GraphicsFormatUtility.GetTextureFormat(gf);
                // texture.format
                newTexture = new Texture2D(texture.width, texture.height, tf, texture.mipmapCount > 0, true);

                // newTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, texture.mipmapCount > 0, true);
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
                if (ApplyInline)
                {
                    // this will remove the rendertexture, etc.
                    ApplyTexture();
                }
                else
                {
                    copiesEnqueued++;
                    renderTexturesToFree.Add(instanceID, texture);
                    QueuedCopies.Enqueue(this);
                }
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

        public static bool SafeToFree(RenderTexture tex)
        {
            int instanceID = tex.GetInstanceID();
            if (renderTexturesToCPU.ContainsKey(instanceID))
            {
                return false;
            }
            if (renderTexturesToFree.ContainsKey(instanceID))
            {
                return false;
            }
            return true;
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
                renderTexturesToFree.Remove(copy.texture.GetInstanceID());
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

                    newTexture.Apply(texture.mipmapCount > 0);  
                    generatedMaterial.material.SetTexture(textureName, newTexture);
                    generatedMaterial.resultingAtlasList[textureIndex] = newTexture;
                    RenderTexture.ReleaseTemporary(texture);
                    renderTexturesCleanedApplied++;
                    texturesUploaded++;
                }
                catch 
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
                RenderTexture.ReleaseTemporary(texture);
                renderTexturesCleanedMissed++;
            }
        }
    }
}
