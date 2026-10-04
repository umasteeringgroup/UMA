using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint.Editor
{
    internal static class TexturePaintGarmentPreview
    {
        internal static Dictionary<TexturePaintChannel, Color[]> Generate(TexturePaintGarmentSettings garment,
            TexturePaintHemSeamSettings hem, Vector2 size, bool closed = false,
            TextureSet set = null, IReadOnlyList<TextureSet> sets = null, TexturePaintLayer owner = null)
        {
            Shader shader = Shader.Find("Hidden/UMA/TexturePaint/GarmentPreview");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Garment preview shader unavailable.");
            garment = garment?.Clone(); hem = hem?.Clone();
            garment?.Normalize(); hem?.Normalize();
            bool isHem = hem?.enabled == true;
            if (!isHem && garment?.enabled != true) return new Dictionary<TexturePaintChannel, Color[]>();
            size = new Vector2(Mathf.Max(.0002f, size.x), Mathf.Max(.0002f, size.y));
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave,
                vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }, triangles = new[] { 0,1,2,0,2,3 } };
            RenderTexture target = null;
            Texture2D readback = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                using var inputs = !isHem && garment.HasReferences && set != null
                    ? new TexturePaintGarmentSettings.Inputs(garment, set, sets, owner) : null;
                int resolution = TexturePaintPreviewDisplay.Resolution;
                target = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                readback = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false, true)
                    { hideFlags = HideFlags.HideAndDontSave };
                var result = new Dictionary<TexturePaintChannel, Color[]>();
                IEnumerable<TexturePaintChannel> channels = isHem ? hem.OutputChannels() : garment.OutputChannels();
                foreach (var channel in channels)
                {
                    var properties = new MaterialPropertyBlock();
                    if (isHem) hem.Bind(properties, channel);
                    else garment.Bind(properties, channel, size, set, sets, owner, inputs);
                    properties.SetInt("_PreviewHem", isHem ? 1 : 0);
                    properties.SetInt("_PreviewClosed", closed ? 1 : 0);
                    properties.SetVector("_PreviewSize", new Vector4(size.x, size.y, 0, 0));
                    float largest = Mathf.Max(size.x, size.y);
                    properties.SetVector("_PreviewFrame", new Vector4(size.x / largest * .9f, size.y / largest * .9f, 0, 0));
                    using (var commands = new CommandBuffer { name = "Garment sample preview" })
                    {
                        commands.SetRenderTarget(target);
                        commands.SetViewport(new Rect(0, 0, resolution, resolution));
                        commands.ClearRenderTarget(false, true, Color.clear);
                        commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, properties);
                        Graphics.ExecuteCommandBuffer(commands);
                    }
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0, false);
                    readback.Apply(false, false);
                    result[channel] = readback.GetPixels();
                }
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
