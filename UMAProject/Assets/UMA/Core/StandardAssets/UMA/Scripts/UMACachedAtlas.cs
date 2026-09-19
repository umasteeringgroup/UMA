using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// One immutable atlas image, optionally transitioning from its completed render texture
    /// to a CPU texture. A readback owns a separate lease, independent of the first avatar.
    /// </summary>
    public sealed class UMACachedAtlas : ScriptableObject
    {
        private static readonly Dictionary<Texture, UMACachedAtlas> owners = new Dictionary<Texture, UMACachedAtlas>();
        public Texture Texture { get; private set; }
        public bool ConversionPending { get; internal set; }
        private bool temporary;
        internal event Action<Texture> Changed;

        internal void Initialize(Texture texture, bool isTemporary)
        {
            hideFlags = HideFlags.HideAndDontSave;
            name = "Shared UMA atlas";
            Texture = texture;
            temporary = isTemporary;
            UMAGeneratedResourceCache.Protect(texture);
            owners.Add(texture, this);
        }

        internal void CompleteConversion(Texture2D texture)
        {
            var old = Texture;
            texture.wrapMode = old.wrapMode; texture.filterMode = old.filterMode;
            texture.anisoLevel = old.anisoLevel; texture.mipMapBias = old.mipMapBias;
            Texture = texture;
            UMAGeneratedResourceCache.Protect(texture);
            owners.Add(texture, this);
            ConversionPending = false;
            // One disposed consumer must not invalidate the image already adopted by
            // the cache, or prevent the other consumers receiving it.
            try
            {
                if (Changed != null) foreach (Action<Texture> callback in Changed.GetInvocationList())
                    try { callback(texture); } catch (Exception exception) { Debug.LogException(exception); }
            }
            finally { ReleaseTexture(old, temporary); temporary = false; }
        }

        internal static void DestroyAtlas(UMACachedAtlas atlas)
        {
            atlas.Changed = null;
            var texture = atlas.Texture; atlas.Texture = null;
            ReleaseTexture(texture, atlas.temporary);
            UMAUtils.DestroySceneObject(atlas);
        }

        private static void ReleaseTexture(Texture texture, bool temporary)
        {
            if (texture == null) return;
            UMAGeneratedResourceCache.Unprotect(texture);
            owners.Remove(texture);
            if (texture is RenderTexture rt)
            {
                if (temporary) { UMARenderTextureTracker.ReleaseTemporary(rt); return; }
                UMARenderTextureTracker.Untrack(rt); rt.Release();
            }
            UMAUtils.DestroySceneObject(texture);
        }

        internal static IDisposable RetainTextureBinding(Texture texture, Material material, string property)
        {
            if (texture == null || !owners.TryGetValue(texture, out var atlas)) return null;
            var lease = UMAGeneratedResourceCache.RetainSharedResource(atlas);
            return lease != null ? new UMAAtlasBinding.RendererUsage(lease, property, new[] { material }) : null;
        }
    }

    internal sealed class UMAAtlasBinding : IDisposable
    {
        private UMAGeneratedResourceCache.Lease<UMACachedAtlas> lease;
        private readonly UMAData.GeneratedMaterial material;
        private readonly int channel;
        private readonly string property;
        private int references = 1;
        internal UMAAtlasBinding(UMAGeneratedResourceCache.Lease<UMACachedAtlas> lease, UMAData.GeneratedMaterial material, int channel, string property)
        {
            this.lease = lease; this.material = material; this.channel = channel; this.property = property;
            lease.Resource.Changed += Apply;
        }
        internal UMAAtlasBinding Retain() { if (lease == null) throw new ObjectDisposedException(nameof(UMAAtlasBinding)); references++; return this; }
        internal UMAGeneratedResourceCache.Lease<UMACachedAtlas> RetainAtlas() => lease.Retain();
        internal bool IsPending => lease?.Resource != null && lease.Resource.ConversionPending;
        internal UMACachedAtlas Atlas => lease?.Resource;
        internal string Property => property;
        internal IDisposable RetainForMaterials(params Material[] materials) => new RendererUsage(lease.Retain(), property, materials);

        internal sealed class RendererUsage : IDisposable
        {
            private UMAGeneratedResourceCache.Lease<UMACachedAtlas> lease;
            private readonly string property;
            private readonly Material[] materials;
            internal RendererUsage(UMAGeneratedResourceCache.Lease<UMACachedAtlas> lease, string property, Material[] materials)
            {
                this.lease = lease; this.property = property; this.materials = materials;
                lease.Resource.Changed += Apply;
            }
            private void Apply(Texture texture)
            {
                if (string.IsNullOrEmpty(property)) return;
                foreach (var material in materials)
                    if (material != null && material.HasProperty(property)) material.SetTexture(property, texture);
            }
            public void Dispose()
            {
                if (lease == null) return;
                lease.Resource.Changed -= Apply; lease.Dispose(); lease = null;
            }
        }
        internal void Apply(Texture texture)
        {
            if (material.resultingAtlasList != null && channel < material.resultingAtlasList.Length)
                material.resultingAtlasList[channel] = texture;
            if (!string.IsNullOrEmpty(property))
            {
                if (material.material != null) material.material.SetTexture(property, texture);
                if (material.secondPassMaterial != null && material.secondPassMaterial.HasProperty(property)) material.secondPassMaterial.SetTexture(property, texture);
            }
        }
        public void Dispose()
        {
            if (lease == null || --references != 0) return;
            lease.Resource.Changed -= Apply;
            lease.Dispose(); lease = null;
        }
    }
}
