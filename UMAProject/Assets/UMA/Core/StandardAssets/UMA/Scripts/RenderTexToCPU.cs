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
        private readonly Action<Texture2D> sharedCompletion;
        private Action sharedFinished;
        private readonly UMAObjectId sourceId;
        private AsyncGPUReadbackRequest readback;
        private bool readbackRequested;
        private readonly GeneratorRuntimeCompression qualityCompression;
        // Set when the atlas is compressed on the GPU and its blocks, not its pixels, are read back.
        private readonly GraphicsFormat gpuFormat;
        private UMAGpuTextureCompressor.ReadbackJob gpuJob;
        // What the final upload still has to do. Compressed data arrives with its mip levels.
        private bool applyMips;
        private readonly bool keepReadable;
        private readonly FilterMode qualityFilter;
        private readonly int qualityAnisotropy;
        private readonly float qualityMipBias;
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
                // Returning a temporary RT while the GPU still reads it permits
                // its storage to be reused by an unrelated render operation.
                if (copy.readbackRequested && !copy.readback.done) copy.readback.WaitForCompletion();
                if (copy.gpuJob != null)
                {
                    // Waits for the block readback, then drops the result with its buffer.
                    var job = copy.gpuJob;
                    copy.gpuJob = null;
                    job.Dispose();
                }
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

        public RenderTexToCPU(RenderTexture texture, GeneratedMaterial generatedMaterial, string textureName, int textureIndex, UMAGeneratorBase basegen,
            Action<Texture2D> sharedCompletion = null, Action sharedFinished = null, GraphicsFormat gpuFormat = GraphicsFormat.None)
        {
            this.texture = texture;
            this.generatedMaterial = generatedMaterial;
            this.textureName = textureName;
            this.textureIndex = textureIndex;
            this.recreateMips = basegen.qualityTextures.Mips(basegen.convertMipMaps);
            this.gpuFormat = gpuFormat;
            applyMips = recreateMips && gpuFormat == GraphicsFormat.None;
            qualityCompression = gpuFormat != GraphicsFormat.None ? GeneratorRuntimeCompression.Disabled :
                basegen.qualityTextures.EffectiveCompression(true, texture.format, texture.width, texture.height);
            keepReadable = basegen.keepConvertedTexturesReadable;
            qualityFilter = texture.filterMode;
            qualityAnisotropy = texture.anisoLevel;
            qualityMipBias = texture.mipMapBias;
            this.sharedCompletion = sharedCompletion;
            this.sharedFinished = sharedFinished;
            this.sourceId = texture.GetUmaObjectId();
            renderTexturesToCPU.Add(texture.GetUmaObjectId(), this);
        }

        public void DoAsyncCopy()
        {
            try
            {
                if (gpuFormat != GraphicsFormat.None)
                {
                    gpuJob = UMAGpuTextureCompressor.CompressWithReadback(texture, gpuFormat, recreateMips, QueueCompressed);
                    if (gpuJob == null)
                    {
                        throw new InvalidOperationException("GPU texture compression to " + gpuFormat + " is not available.");
                    }
                    return;
                }
                readback = AsyncGPUReadback.Request(texture, 0, (AsyncGPUReadbackRequest asyncAction) =>
                {
                    QueueCopy(asyncAction);
                });
                readbackRequested = true;
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
            if (sharedCompletion != null || (generatedMaterial != null && generatedMaterial.material != null))
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
                    newTexture = new Texture2D(texture.width, texture.height, tf, recreateMips, true);
                    newTexture.filterMode = qualityFilter;
                    newTexture.anisoLevel = qualityAnisotropy;
                    newTexture.mipMapBias = qualityMipBias;
                    newTexture.wrapMode = TextureWrapMode.Repeat;

                    newTexture.SetPixelData(asyncAction.GetData<byte>(), 0);
                    QueueApply(entityId);
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

        // The GPU-compressed counterpart of QueueCopy: the job hands over a texture that
        // already holds the compressed mip levels and only needs its upload.
        private void QueueCompressed(UMAGpuTextureCompressor.ReadbackJob job)
        {
            Texture2D compressed = job.TakeTexture();
            gpuJob = null;
            if (sourceTextureReleased || texture == null)
            {
                UMAUtils.DestroySceneObject(compressed);
                return;
            }

            var entityId = texture.GetUmaObjectId();
            renderTexturesToCPU.Remove(entityId);

            if (compressed == null)
            {
                errorUploads++;
                ReleaseSourceTexture();
                renderTexturesCleanedMissed++;
                return;
            }

            newTexture = compressed;
            if (sharedCompletion != null || (generatedMaterial != null && generatedMaterial.material != null))
            {
                try
                {
                    newTexture.filterMode = qualityFilter;
                    newTexture.anisoLevel = qualityAnisotropy;
                    newTexture.mipMapBias = qualityMipBias;
                    newTexture.wrapMode = TextureWrapMode.Repeat;
                    QueueApply(entityId);
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
                DestroyNewTexture();
                ReleaseSourceTexture();
                renderTexturesCleanedMissed++;
            }
        }

        // Applies the finished texture now, or queues it for the generator's update loop.
        private void QueueApply(UMAObjectId entityId)
        {
#if UNITY_EDITOR
            // We can't count on the generator update loop while editing.
            if (!Application.isPlaying)
            {
                ApplyTexture();
                return;
            }
#endif
            var generator = UMAAssetIndexer.bareInstance != null ? UMAAssetIndexer.bareInstance.bareGenerator : null;
            if (ApplyInline || sharedCompletion != null && (generator == null || !generator.isActiveAndEnabled))
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
            if (sharedCompletion != null)
            {
                try
                {
                    GeneratorTextureQuality.FinishConvertedTexture(newTexture, applyMips, qualityCompression, keepReadable);
                    sharedCompletion(newTexture);
                    newTexture = null; // ownership transferred to the atlas cache
                    texturesUploaded++;
                }
                catch { errorUploads++; DestroyNewTexture(); }
                finally { ReleaseSourceTexture(); }
                return;
            }
            if (generatedMaterial != null && generatedMaterial.material != null)
            {
                try
                {
                    if (newTexture == null || texture == null || generatedMaterial.resultingAtlasList == null ||
                        textureIndex < 0 || textureIndex >= generatedMaterial.resultingAtlasList.Length)
                    {
                        throw new InvalidOperationException("Asynchronous atlas copy target is no longer valid.");
                    }

                    GeneratorTextureQuality.FinishConvertedTexture(newTexture, applyMips, qualityCompression, keepReadable);
                    generatedMaterial.material.SetTexture(textureName, newTexture);
                    generatedMaterial.resultingAtlasList[textureIndex] = newTexture;
                    if (generatedMaterial.skinnedMeshRenderer != null &&
                        generatedMaterial.skinnedMeshRenderer.TryGetComponent<UMAResourceLeaseOwner>(out var owner))
                        owner.AdoptPrivateStaticResources(generatedMaterial);
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
            if (sharedCompletion != null)
            {
                renderTexturesToCPU.Remove(sourceId);
                renderTexturesToFree.Remove(sourceId);
                texture = null; // the atlas owns the source, including failure fallback
                var finished = sharedFinished; sharedFinished = null;
                finished?.Invoke();
                return;
            }
            if (texture == null)
            {
                return;
            }

            UMAObjectId entityId = texture.GetUmaObjectId();
            renderTexturesToCPU.Remove(entityId);
            renderTexturesToFree.Remove(entityId);
            // When the copy did not replace the atlas entry (it was cancelled or failed), the
            // atlas still points at this texture. It is going back to Unity's pool, so the atlas
            // must forget it, or UMAData teardown would later try to destroy a pooled texture.
            Texture[] atlas = generatedMaterial?.resultingAtlasList;
            if (atlas != null && textureIndex >= 0 && textureIndex < atlas.Length &&
                ReferenceEquals(atlas[textureIndex], texture))
            {
                atlas[textureIndex] = null;
            }
            UMARenderTextureTracker.ReleaseTemporary(texture);
            texture = null;
        }
    }
}
