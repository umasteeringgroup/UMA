using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace UMA.TexturePaint
{
    public static class TexturePaintRegionRenderer
    {
        public static TexturePaintRegion Polygon(int width,int height,IReadOnlyList<Vector2> polygon)
        {
            var pixels=new byte[width*height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                if(TexturePaintRegion.Contains(polygon,new Vector2((x+.5f)/width,(y+.5f)/height)))pixels[y*width+x]=255;
            return TexturePaintRegion.Encode(width,height,pixels);
        }
        // Camera selection is rasterized into each tile's UV space, with an optional visibility test.
        public static Dictionary<TextureSet,TexturePaintRegion> Project(IReadOnlyList<TextureSet> sets,Camera camera,
            IReadOnlyList<Vector2> polygon,bool through)
        {
            var result=new Dictionary<TextureSet,TexturePaintRegion>();
            Shader shader=Shader.Find("Hidden/UMA/TexturePaint/RegionSelection");
            if(shader==null || !shader.isSupported)throw new InvalidOperationException("Selection shader is unavailable.");
            var material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            Texture2D shape=Polygon(512,512,polygon).CreateTexture();
            var depth=RenderTexture.GetTemporary(1024,1024,24,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear);
            try
            {
                material.SetMatrix("_SelectionVP",camera.projectionMatrix*camera.worldToCameraMatrix);
                material.SetMatrix("_SelectionView",camera.worldToCameraMatrix);
                material.SetVector("_ClipPlanes",new Vector4(camera.nearClipPlane,camera.farClipPlane,0,0));
                material.SetTexture("_SelectionShape",shape);material.SetTexture("_SelectionDepth",depth);material.SetInt("_Through",through?1:0);
                using(var command=new CommandBuffer())
                {
                    command.SetRenderTarget(depth);command.ClearRenderTarget(true,true,new Color(camera.farClipPlane,0,0,1));
                    foreach(var set in sets)Draw(command,set,material,0);
                    Graphics.ExecuteCommandBuffer(command);
                }
                foreach(var set in sets)
                {
                    TextureChannelTarget channel=null;foreach(var candidate in set.channels.Values){channel=candidate;break;}
                    if(channel?.Texture==null)continue;
                    var output=RenderTexture.GetTemporary(channel.Texture.width,channel.Texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
                    try
                    {
                        using(var command=new CommandBuffer())
                        {command.SetRenderTarget(output);command.ClearRenderTarget(false,true,Color.clear);Draw(command,set,material,1);Graphics.ExecuteCommandBuffer(command);}
                        result[set]=Read(output);
                    }
                    finally{RenderTexture.ReleaseTemporary(output);}
                }
                return result;
            }
            finally{UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(shape);RenderTexture.ReleaseTemporary(depth);}
        }
        private static void Draw(CommandBuffer command,TextureSet set,Material material,int pass)
        {
            Mesh mesh=set.surface?.mesh;if(mesh==null)return;
            for(int sub=0;sub<mesh.subMeshCount;sub++)command.DrawMesh(mesh,TexturePaintProjectionGeometry.LocalToWorld(set),material,sub,pass);
        }
        public static TexturePaintRegion Read(RenderTexture source)
        {
            var previous=RenderTexture.active;var texture=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);
            try
            {
                RenderTexture.active=source;texture.ReadPixels(new Rect(0,0,source.width,source.height),0,0);texture.Apply();
                var colors=texture.GetPixels32();var values=new byte[colors.Length];for(int i=0;i<values.Length;i++)values[i]=colors[i].r;
                return TexturePaintRegion.Encode(source.width,source.height,values);
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
