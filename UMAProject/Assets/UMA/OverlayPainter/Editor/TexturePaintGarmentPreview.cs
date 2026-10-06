using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint.Editor
{
    internal static class TexturePaintGarmentPreview
    {
        internal static Dictionary<TexturePaintChannel, Color[]> Generate(TexturePaintGarmentSettings garment,
            TexturePaintHemSeamSettings hem, Vector2 size, bool closed = false,
            TextureSet set = null, IReadOnlyList<TextureSet> sets = null, TexturePaintLayer owner = null,
            TexturePaintPathGeneratorSettings pathGenerator = null)
        {
            Shader shader = Shader.Find("Hidden/UMA/TexturePaint/GarmentPreview");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Garment preview shader unavailable.");
            garment = garment?.Clone(); hem = hem?.Clone();
            garment?.Normalize(); hem?.Normalize();
            bool isHem = hem?.enabled == true;
            if (!isHem && garment?.enabled != true && pathGenerator?.enabled != true) return new Dictionary<TexturePaintChannel, Color[]>();
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
                pathGenerator=pathGenerator?.Clone();pathGenerator?.Normalize();
                var generator=pathGenerator?.enabled==true ? TexturePaintPathGenerators.Find(pathGenerator.generatorId) : null;
                if(pathGenerator?.enabled==true && generator==null)throw new InvalidOperationException("Path generator is not installed.");
                using var prepared=generator?.Prepare(pathGenerator);
                using var inputs = !isHem && garment?.HasReferences == true && set != null
                    ? new TexturePaintGarmentSettings.Inputs(garment, set, sets, owner) : null;
                int resolution = TexturePaintPreviewDisplay.Resolution;
                target = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                readback = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false, true)
                    { hideFlags = HideFlags.HideAndDontSave };
                var result = new Dictionary<TexturePaintChannel, Color[]>();
                IEnumerable<TexturePaintChannel> channels = generator!=null ?
                    owner?.channels.Keys.ToArray() ?? new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl} :
                    isHem ? hem.OutputChannels() : garment.OutputChannels();
                foreach (var channel in channels)
                {
                    var properties = new MaterialPropertyBlock();
                    if (generator!=null)generator.Bind(properties,pathGenerator,prepared,channel,
                        owner?.GetChannelSettings(channel,false)?.sourceSettings?.color ?? Color.white,size);
                    else if (isHem) hem.Bind(properties, channel);
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
                    result[channel] = generator!=null && owner?.effects?.HasEnabled==true
                        ? ApplyLayerEffects(target,owner,channel) : readback.GetPixels();
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

        internal static Color[] ApplyLayerEffects(Texture source,TexturePaintLayer owner,TexturePaintChannel channel)
        {
            var previous = RenderTexture.active;
            var compute=UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Shaders/LayerComposite.compute"));
            using var compositor=new TextureLayerCompositor(compute);
            using var raster=new EditableTextureTarget("Generator preview source",source.width,source.height,RenderTextureFormat.ARGBFloat,source,Color.clear);
            using var output=new EditableTextureTarget("Generator preview effects",source.width,source.height,RenderTextureFormat.ARGBFloat,null,Color.clear);
            using var previewSet=new TextureSet();
            using var layer=new TexturePaintLayer { effects=owner.effects.Clone() };
            float scale=owner.channels.TryGetValue(channel,out var authored) ? (float)source.width/Mathf.Max(source.width,authored.Width) : 1;
            foreach(var effect in layer.effects.Stack)
            {
                effect.width*=scale;effect.offset*=scale;
                effect.contourThreadWidth*=scale;effect.contourStitchLength*=scale;effect.contourStitchInset*=scale;
            }
            if(!compositor.CompositeLayerInto(output.Front,previewSet,layer,raster,channel,1,TexturePaintBlendMode.Normal))
                throw new InvalidOperationException("Generator layer-effect preview is unavailable.");
            compositor.UnassociateAlpha(output.Front);
            var readback=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            try
            {
                RenderTexture.active=output.Front;readback.ReadPixels(new Rect(0,0,source.width,source.height),0,0);
                readback.Apply();return readback.GetPixels();
            }
            finally { RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(readback); }
        }
    }
}
