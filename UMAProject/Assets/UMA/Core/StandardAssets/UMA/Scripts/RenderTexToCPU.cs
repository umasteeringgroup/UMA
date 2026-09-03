using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UMA;
using static UMA.UMAData;
using UnityEngine.Rendering;
using System.Collections.Concurrent;
using UnityEngine.Experimental.Rendering;

namespace UMA
{
    public class RenderTexToCPU
    {
        public static bool ApplyInline;
        public static Dictionary<UMAObjectId, RenderTexToCPU> renderTexturesToCPU = new Dictionary<UMAObjectId, RenderTexToCPU>();
        public static Queue<RenderTexToCPU> QueuedCopies = new Queue<RenderTexToCPU>();
        public static Dictionary<UMAObjectId, RenderTexture> renderTexturesToFree = new Dictionary<UMAObjectId, RenderTexture>();

        public RenderTexture texture;
        public GeneratedMaterial generatedMaterial;
        public string textureName;
        public int textureIndex;
        public Texture2D newTexture;
        public bool recreateMips;
        private bool sourceTextureReleased;
        public static int copiesEnqueued = 0;
        public static int copiesDequeued = 0;
        public static int unableToQueue = 0;
        public static int misseduploads = 0;
        public static int errorUploads = 0;
        public static int texturesUploaded = 0;
        public static int renderTexturesCleanedUMAData = 0;
        public static int renderTexturesCleanedApplied = 0;
        public static int renderTexturesCleanedMissed = 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void StaticInitializeOnLoad()
        {
            CleanupPendingCopies();
            renderTexturesToCPU = new Dictionary<UMAObjectId, RenderTexToCPU>();
            renderTexturesToFree = new Dictionary<UMAObjectId, RenderTexture>();
            QueuedCopies = new Queue<RenderTexToCPU>();
            ApplyInline = false;
            copiesEnqueued = 0;
            copiesDequeued = 0;
            unableToQueue = 0;
            misseduploads = 0;
            errorUploads = 0;
            texturesUploaded = 0;
            renderTexturesCleanedUMAData = 0;
            renderTexturesCleanedApplied = 0;
            renderTexturesCleanedMissed = 0;
        }

        /// <summary>
        /// Cancels retained asynchronous atlas-copy state and releases every
        /// temporary texture it still owns. This is safe to call repeatedly at
        /// editor play-mode boundaries.
        /// </summary>
        public static void CleanupPendingCopies()
        {
            var pendingCopies = new HashSet<RenderTexToCPU>();
            if (renderTexturesToCPU != null)
            {
                foreach (RenderTexToCPU copy in renderTexturesToCPU.Values)
                {
                    if (copy != null)
                    {
                        pendingCopies.Add(copy);
                    }
                }
            }
            if (QueuedCopies != null)
            {
                foreach (RenderTexToCPU copy in QueuedCopies)
                {
                    if (copy != null)
                    {
                        pendingCopies.Add(copy);
                    }
                }
            }

            foreach (RenderTexToCPU copy in pendingCopies)
            {
                copy.DestroyNewTexture();
                copy.ReleaseSourceTexture();
            }

            // A texture can be retained for release after its copy object has
            // already left the queue. Release any such remainder explicitly.
            if (renderTexturesToFree != null)
            {
                var remainingTextures =
                    new List<RenderTexture>(renderTexturesToFree.Values);
                for (int i = 0; i < remainingTextures.Count; i++)
                {
                    RenderTexture texture = remainingTextures[i];
                    if (texture != null)
                    {
                        UMARenderTextureTracker.ReleaseTemporary(texture);
                    }
                }
            }

            renderTexturesToCPU?.Clear();
            renderTexturesToFree?.Clear();
            QueuedCopies?.Clear();
        }

        public RenderTexToCPU(RenderTexture texture, GeneratedMaterial generatedMaterial, string textureName, int textureIndex, UMAGeneratorBase basegen)
        {
            this.texture = texture;
            this.generatedMaterial = generatedMaterial;
            this.textureName = textureName;
            this.textureIndex = textureIndex;
            this.recreateMips = basegen.convertMipMaps;
            renderTexturesToCPU.Add(texture.GetUmaObjectId(), this);
        }

        public void DoAsyncCopy()
        {
            try
            {
                AsyncGPUReadback.Request(texture, 0, (AsyncGPUReadbackRequest asyncAction) =>
                {
                    QueueCopy(asyncAction);
                });
            }
            catch
            {
                errorUploads++;
                ReleaseSourceTexture();
                renderTexturesCleanedMissed++;
            }
        }


        private void QueueCopy(AsyncGPUReadbackRequest asyncAction)
        {
            if (sourceTextureReleased || texture == null)
            {
                return;
            }

            var entityId = texture.GetUmaObjectId();
            renderTexturesToCPU.Remove(entityId);

            // if it's still valid, then create the texture and enqueue the apply method
            if (generatedMaterial != null && generatedMaterial.material != null)
            {
                try
                {
                    if (asyncAction.hasError)
                    {
                        errorUploads++;
                        ReleaseSourceTexture();
                        renderTexturesCleanedMissed++;
                        return;
                    }

                    var w = asyncAction.width;
                    var h = asyncAction.height;

                    if (w != texture.width || h != texture.height)
                    {
#if UNITY_EDITOR
                        Debug.LogWarning("Texture size changed during copy; discarding the asynchronous atlas copy.");
#endif
                        errorUploads++;
                        ReleaseSourceTexture();
                        renderTexturesCleanedMissed++;
                        return;
                    }

                    GraphicsFormat gf = GraphicsFormatUtility.GetGraphicsFormat(texture.format,false);
                    TextureFormat tf = GraphicsFormatUtility.GetTextureFormat(gf);
                    newTexture = new Texture2D(texture.width, texture.height, tf, texture.mipmapCount > 0, true);

                    newTexture.SetPixelData(asyncAction.GetData<byte>(), 0);
#if UNITY_EDITOR
                    // We can't count on the generator update loop while editing.
                    if (!Application.isPlaying)
                    {
                        ApplyTexture();
                        return;
                    }
#endif
                    if (ApplyInline)
                    {
                        ApplyTexture();
                    }
                    else
                    {
                        copiesEnqueued++;
                        renderTexturesToFree.Add(entityId, texture);
                        QueuedCopies.Enqueue(this);
                    }
                }
                catch
                {
                    errorUploads++;
                    DestroyNewTexture();
                    ReleaseSourceTexture();
                    renderTexturesCleanedMissed++;
                }
            }
            else
            {
                unableToQueue++;
                ReleaseSourceTexture();
                renderTexturesCleanedMissed++;
            }
        }

        public static int PendingCopies()
        {
            return QueuedCopies.Count;
        }

        public static bool SafeToFree(RenderTexture tex)
        {
            var entityId = tex.GetUmaObjectId();
            if (renderTexturesToCPU.ContainsKey(entityId))
            {
                return false;
            }
            if (renderTexturesToFree.ContainsKey(entityId))
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
                    if (newTexture == null || texture == null || generatedMaterial.resultingAtlasList == null ||
                        textureIndex < 0 || textureIndex >= generatedMaterial.resultingAtlasList.Length)
                    {
                        throw new InvalidOperationException("Asynchronous atlas copy target is no longer valid.");
                    }

                    newTexture.Apply(texture.mipmapCount > 0);  
                    generatedMaterial.material.SetTexture(textureName, newTexture);
                    generatedMaterial.resultingAtlasList[textureIndex] = newTexture;
                    renderTexturesCleanedApplied++;
                    texturesUploaded++;
                }
                catch 
                {
                    errorUploads++;
                    DestroyNewTexture();
                    renderTexturesCleanedMissed++;
                }
                finally
                {
                    ReleaseSourceTexture();
                }
            }
            else
            {
                misseduploads++;
                DestroyNewTexture();
                ReleaseSourceTexture();
                renderTexturesCleanedMissed++;
            }
        }

        private void DestroyNewTexture()
        {
            if (newTexture != null)
            {
                UMAUtils.DestroySceneObject(newTexture);
                newTexture = null;
            }
        }

        private void ReleaseSourceTexture()
        {
            if (sourceTextureReleased)
            {
                return;
            }

            sourceTextureReleased = true;
            if (texture == null)
            {
                return;
            }

            UMAObjectId entityId = texture.GetUmaObjectId();
            renderTexturesToCPU.Remove(entityId);
            renderTexturesToFree.Remove(entityId);
            UMARenderTextureTracker.ReleaseTemporary(texture);
            texture = null;
        }
    }
}
