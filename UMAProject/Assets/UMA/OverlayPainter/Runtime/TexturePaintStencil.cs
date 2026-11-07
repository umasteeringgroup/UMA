using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint
{
    [Serializable]
    public sealed class TexturePaintStencil
    {
        public bool enabled, invert;
        public Texture2D texture;
        public TexturePaintLayerMaskTextureChannel channel = TexturePaintLayerMaskTextureChannel.Luminance;
        // Viewport coordinates have their origin at the bottom left.
        public Vector2 center = new Vector2(.5f, .5f), size = new Vector2(.5f, .5f);
        public float rotation, viewportAspect = 1;
        public Matrix4x4 viewProjection = Matrix4x4.identity;
        public bool IsActive => enabled && texture != null;
        public TexturePaintStencil Clone() => (TexturePaintStencil)MemberwiseClone();
        public void Normalize()
        {
            if (!float.IsFinite(center.x) || !float.IsFinite(center.y)) center = new Vector2(.5f, .5f);
            size = new Vector2(float.IsFinite(size.x) ? Mathf.Clamp(size.x, .01f, 8) : .5f,
                float.IsFinite(size.y) ? Mathf.Clamp(size.y, .01f, 8) : .5f);
            rotation = float.IsFinite(rotation) ? rotation : 0;
            viewportAspect = float.IsFinite(viewportAspect) ? Mathf.Max(.001f, viewportAspect) : 1;
        }
    }
    /// <summary>Projects one fixed camera-space stencil into each target's UVs once per stroke.</summary>
    public sealed class TexturePaintStencilRenderer : IDisposable
    {
        private readonly Material material;
        private readonly Dictionary<(TextureSet, int, int), RenderTexture> masks = new();
        public TexturePaintStencilRenderer()
        {
            var shader = Shader.Find("Hidden/UMA/TexturePaint/Stencil");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("The stencil shader is unavailable.");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        public Texture GetMask(TexturePaintStencil stencil, TextureSet set, int width, int height)
        {
            if (stencil?.IsActive != true) return Texture2D.whiteTexture;
            var key = (set, width, height); if (masks.TryGetValue(key, out var cached)) return cached;
            if (set?.surface?.mesh == null) throw new InvalidOperationException("A camera stencil requires a paintable mesh.");
            stencil.Normalize();
            var output = new RenderTexture(width, height, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Stroke Stencil" };
            output.Create(); masks.Add(key, output);
            material.SetTexture("_Stencil", stencil.texture); material.SetMatrix("_StencilVP", stencil.viewProjection);
            material.SetVector("_StencilTransform", new Vector4(stencil.center.x, stencil.center.y, stencil.size.x, stencil.size.y));
            material.SetFloat("_StencilAspect", stencil.viewportAspect);
            material.SetFloat("_StencilRotation", stencil.rotation * Mathf.Deg2Rad);
            material.SetInt("_StencilChannel", (int)stencil.channel); material.SetInt("_StencilInvert", stencil.invert ? 1 : 0);
            using var commands = new CommandBuffer { name = "Project paint stencil" };
            commands.SetRenderTarget(output); commands.ClearRenderTarget(false, true, Color.clear);
            var mesh = set.surface.mesh;
            for (int sub = 0; sub < mesh.subMeshCount; sub++) commands.DrawMesh(mesh, TexturePaintProjectionGeometry.LocalToWorld(set), material, sub, 0);
            Graphics.ExecuteCommandBuffer(commands);
            return output;
        }
        public void Dispose()
        {
            foreach (var mask in masks.Values) if (mask != null) UnityEngine.Object.DestroyImmediate(mask);
            masks.Clear(); if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
