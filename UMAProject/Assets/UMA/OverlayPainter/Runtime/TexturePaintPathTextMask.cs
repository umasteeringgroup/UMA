using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>One glyph coverage image shared by every material channel. Main thread only.</summary>
    public sealed class TexturePaintPathTextMask : IDisposable
    {
        public Texture2D Texture { get; private set; }
        public TexturePaintPathTextMask(string text, Font font, FontStyle style)
        {
            const int size=96, padding=2;
            text=(text??string.Empty).Replace("\r",string.Empty).Replace("\t","    ");
            if(text.Length>512)text=text.Substring(0,512);
            if(string.IsNullOrWhiteSpace(text))
            {
                Texture=new Texture2D(1,1,TextureFormat.RGBA32,false,true)
                { name="Empty Tattoo",hideFlags=HideFlags.HideAndDontSave };
                Texture.SetPixel(0,0,Color.clear);Texture.Apply();return;
            }
            font=font!=null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if(font==null)throw new InvalidOperationException("Assign a Font to the Tattoo generator.");
            font.RequestCharactersInTexture(text,size,style);
            // Capture metrics and atlas together after the request; a later request may repack it.
            var glyphs=new List<(CharacterInfo glyph, int x, int y)>();
            int pen=0,baseline=0,minX=0,minY=0,maxX=1,maxY=1;
            int lineHeight=size+size/4;
            foreach(char character in text)
            {
                if(character=='\n'){pen=0;baseline-=lineHeight;continue;}
                if(!font.GetCharacterInfo(character,out var g,size,style))
                    throw new InvalidOperationException($"The selected Font has no glyph for '{character}'. Choose another Font.");
                glyphs.Add((g,pen,baseline));
                minX=Math.Min(minX,pen+g.minX);maxX=Math.Max(maxX,pen+g.maxX);
                minY=Math.Min(minY,baseline+g.minY);maxY=Math.Max(maxY,baseline+g.maxY);
                pen+=g.advance;
            }
            Texture2D atlas=font.material?.mainTexture as Texture2D;
            if(atlas==null)throw new InvalidOperationException("The selected Font has no glyph atlas.");
            float scale=Mathf.Min(1,8190f/(maxX-minX),2046f/(maxY-minY));
            int width=Mathf.Max(1,Mathf.CeilToInt((maxX-minX)*scale))+padding*2;
            int height=Mathf.Max(1,Mathf.CeilToInt((maxY-minY)*scale))+padding*2;
            Texture2D copy=null;
            var rt=RenderTexture.GetTemporary(atlas.width,atlas.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            try
            {
                Graphics.Blit(atlas,rt);RenderTexture.active=rt;
                copy=new Texture2D(atlas.width,atlas.height,TextureFormat.RGBA32,false,true);
                copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();
                var pixels=new Color32[width*height];
                foreach(var item in glyphs)
                {
                    var g=item.glyph;
                    int x0=padding+Mathf.FloorToInt((item.x+g.minX-minX)*scale);
                    int x1=padding+Mathf.CeilToInt((item.x+g.maxX-minX)*scale);
                    int y0=padding+Mathf.FloorToInt((item.y+g.minY-minY)*scale);
                    int y1=padding+Mathf.CeilToInt((item.y+g.maxY-minY)*scale);
                    for(int y=y0;y<y1;y++) for(int x=x0;x<x1;x++)
                    {
                        float tx=(x+.5f-x0)/Math.Max(1,x1-x0),ty=(y+.5f-y0)/Math.Max(1,y1-y0);
                        var uv=Vector2.Lerp(Vector2.Lerp(g.uvBottomLeft,g.uvBottomRight,tx),
                            Vector2.Lerp(g.uvTopLeft,g.uvTopRight,tx),ty);
                        byte alpha=(byte)Mathf.RoundToInt(copy.GetPixelBilinear(uv.x,uv.y).a*255);
                        int index=y*width+x;
                        if(x>=0&&x<width&&y>=0&&y<height&&alpha>pixels[index].a)
                            pixels[index]=new Color32(255,255,255,alpha);
                    }
                }
                Texture=new Texture2D(width,height,TextureFormat.RGBA32,true,true)
                { name="Path Tattoo Coverage",hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear };
                Texture.SetPixels32(pixels);Texture.Apply(true,false);
            }
            finally
            {
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
                if(copy!=null)UnityEngine.Object.DestroyImmediate(copy);
            }
        }
        public void Dispose(){if(Texture!=null)UnityEngine.Object.DestroyImmediate(Texture);Texture=null;}
    }
}
