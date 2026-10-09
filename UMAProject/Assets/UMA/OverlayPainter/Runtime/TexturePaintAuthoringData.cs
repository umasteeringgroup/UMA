using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace UMA.TexturePaint
{
    public enum TexturePaintReferenceComponent { Color, Alpha, Luminance, Red, Green, Blue, Mask }
    [Serializable]
    public sealed class TexturePaintLayerReference
    {
        public string surfaceId, layerId, logicalLayerId, paintTargetId;
        public TexturePaintChannel channel = TexturePaintChannel.Albedo;
        public TexturePaintReferenceComponent component = TexturePaintReferenceComponent.Alpha;
        public bool invert;
        public bool IsSet => !string.IsNullOrEmpty(layerId) || !string.IsNullOrEmpty(logicalLayerId);
        public TexturePaintLayerReference Clone() => (TexturePaintLayerReference)MemberwiseClone();
        public static TexturePaintLayerReference To(TextureSet set, TexturePaintLayer layer) => new TexturePaintLayerReference
        { surfaceId = set.persistentId, layerId = layer.id, logicalLayerId = layer.logicalLayerId, paintTargetId = layer.paintTargetId };
    }
    [Serializable]
    public sealed class TexturePaintLayerLinks
    {
        public string anchorName;
        public TexturePaintLayerReference content, mask, instance;
        public TexturePaintChannel outputChannel = TexturePaintChannel.Albedo;
        public Color tint = Color.white;
        public Vector2 tiling = Vector2.one, offset;
        public float rotation;
        public bool HasLinks => content?.IsSet == true || mask?.IsSet == true || instance?.IsSet == true;
        public TexturePaintLayerLinks Clone() => new TexturePaintLayerLinks
        { anchorName = anchorName, content = content?.Clone(), mask = mask?.Clone(), instance = instance?.Clone(),
            outputChannel = outputChannel, tint = tint, tiling = tiling, offset = offset, rotation = rotation };
    }
    [Serializable]
    public sealed class TexturePaintSymmetry
    {
        public bool enabled, mirrorX = true, mirrorY, mirrorZ;
        public Vector3 origin, euler;
        public int radialCopies = 1;
        public Vector3 radialAxis = Vector3.up;
        [HideInInspector] public bool legacyWorldMirrorOrder;
        public TexturePaintSymmetry Clone() => (TexturePaintSymmetry)MemberwiseClone();
        public List<Matrix4x4> Transforms(bool uvSpace = false)
        {
            var result = new List<Matrix4x4>();
            if (!enabled) { result.Add(Matrix4x4.identity); return result; }
            Vector3 pivot = uvSpace ? new Vector3(0.5f + origin.x, 0.5f + origin.y, 0) : origin;
            Quaternion orientation = uvSpace ? Quaternion.Euler(0,0,euler.z) : Quaternion.Euler(euler);
            Matrix4x4 frame = Matrix4x4.TRS(pivot, orientation, Vector3.one);
            Vector3 axis = uvSpace ? Vector3.forward : radialAxis.sqrMagnitude > 1e-8f ? radialAxis.normalized : Vector3.up;
            int copies = Mathf.Clamp(radialCopies, 1, 16);
            if(legacyWorldMirrorOrder)
            {
                // Old paths rotated about their character root, then mirrored about world X.
                // Preserve that placement until the saved layer frame itself is edited.
                var reflection=Matrix4x4.TRS(uvSpace ? Vector3.right : Vector3.zero,Quaternion.identity,new Vector3(-1,1,1));
                for(int n=0;n<copies;n++)
                {
                    var rotation=Matrix4x4.Translate(pivot)*Matrix4x4.Rotate(Quaternion.AngleAxis(n*360f/copies,axis))*Matrix4x4.Translate(-pivot);
                    result.Add(rotation);if(mirrorX)result.Add(reflection*rotation);
                }
                return result;
            }
            for (int n = 0; n < copies; n++)
            for (int bits = 0; bits < 8; bits++)
            {
                if (((bits & 1) != 0 && !mirrorX) || ((bits & 2) != 0 && !mirrorY) || ((bits & 4) != 0 && (!mirrorZ || uvSpace))) continue;
                Matrix4x4 transform = frame * Matrix4x4.Rotate(Quaternion.AngleAxis(n * 360f / copies, axis)) *
                    Matrix4x4.Scale(new Vector3((bits & 1) != 0 ? -1 : 1, (bits & 2) != 0 ? -1 : 1, (bits & 4) != 0 ? -1 : 1)) * frame.inverse;
                bool duplicate = false;
                foreach (Matrix4x4 prior in result)
                {
                    float difference = 0; for (int i = 0; i < 16; i++) difference += Mathf.Abs(prior[i] - transform[i]);
                    if (difference < 0.0001f) { duplicate = true; break; }
                }
                if (!duplicate) result.Add(transform);
            }
            return result;
        }
        public static TexturePaintProjectionSettings TransformProjection(TexturePaintProjectionSettings source, Matrix4x4 transform)
        {
            var copy = source.Clone();
            Vector3 normal = transform.MultiplyVector(source.rotation * Vector3.forward).normalized;
            Vector3 up = transform.MultiplyVector(source.rotation * Vector3.up).normalized;
            copy.position = transform.MultiplyPoint3x4(source.position); copy.rotation = Quaternion.LookRotation(normal, up);
            bool reflected = transform.determinant < 0;
            for (int i = 0; i < source.points.Length; i++)
            {
                int n = source.gridSize;
                int from = reflected ? (i / n) * n + n - 1 - i % n : i;
                copy.pinned[i] = source.pinned[from];
                copy.points[i] = copy.WorldToPoint(transform.MultiplyPoint3x4(source.PointToWorld(source.points[from])));
            }
            if (reflected) copy.flipX = !copy.flipX;
            // Region identity is resolved again by the caller at the transformed placement.
            copy.regionSurfaceId = null; copy.regionTriangle = -1;
            return copy;
        }
    }
    public enum TexturePaintRegionCombine { Replace, Add, Subtract, Intersect }
    [Serializable]
    public sealed class TexturePaintRegion
    {
        public string id = Guid.NewGuid().ToString("N"), name = "Selection";
        public int width, height;
        public byte[] compressed;
        public bool IsValid => width > 0 && height > 0 && width <= 16384 && height <= 16384 && compressed?.Length > 0;
        public TexturePaintRegion Clone() => !IsValid ? null : new TexturePaintRegion { id = id, name = name, width = width, height = height,
            compressed = compressed != null ? (byte[])compressed.Clone() : null };
        public byte[] Decode()
        {
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384) return Array.Empty<byte>();
            var result = new byte[checked(width * height)];
            if (compressed == null || compressed.Length == 0) return result;
            using var input = new MemoryStream(compressed); using var stream = new DeflateStream(input, CompressionMode.Decompress);
            int offset = 0;
            while (offset < result.Length) { int count = stream.Read(result, offset, result.Length - offset); if (count == 0) throw new InvalidDataException("Selection data is incomplete."); offset += count; }
            return result;
        }
        public static TexturePaintRegion Encode(int width, int height, byte[] pixels, string name = "Selection")
        {
            if (pixels == null || pixels.Length != checked(width * height)) throw new ArgumentException("Selection dimensions do not match its pixels.");
            using var output = new MemoryStream();
            using (var stream = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true)) stream.Write(pixels, 0, pixels.Length);
            return new TexturePaintRegion { width = width, height = height, compressed = output.ToArray(), name = name };
        }
        public Texture2D CreateTexture()
        {
            byte[] values = Decode(); var pixels = new Color32[values.Length];
            for (int i=0;i<values.Length;i++) pixels[i] = new Color32(values[i],values[i],values[i],values[i]);
            var texture = new Texture2D(width,height,TextureFormat.RGBA32,false,true)
            { name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false,false); return texture;
        }
        public static byte Combine(byte previous, byte next, TexturePaintRegionCombine operation)
        {
            switch (operation)
            {
                case TexturePaintRegionCombine.Add: return Math.Max(previous,next);
                case TexturePaintRegionCombine.Subtract: return (byte)(previous * (255-next) / 255);
                case TexturePaintRegionCombine.Intersect: return (byte)(previous * next / 255);
                default: return next;
            }
        }
        public static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            bool inside=false;
            for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
            {
                Vector2 a=polygon[i],b=polygon[j];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x) inside=!inside;
            }
            return inside;
        }
        public TexturePaintRegion Adjust(int growPixels, int featherPixels, bool invert)
        {
            byte[] pixels=Decode(); int radius=Mathf.Clamp(Mathf.Abs(growPixels),0,256);
            if(radius>0) { pixels=Filter(pixels,width,height,radius,growPixels>0?1:-1,true); pixels=Filter(pixels,width,height,radius,growPixels>0?1:-1,false); }
            radius=Mathf.Clamp(featherPixels,0,256);
            if(radius>0) { pixels=Filter(pixels,width,height,radius,0,true); pixels=Filter(pixels,width,height,radius,0,false); }
            if(invert) for(int i=0;i<pixels.Length;i++) pixels[i]=(byte)(255-pixels[i]);
            var result=Encode(width,height,pixels,name);result.id=id;return result;
        }
        private static byte[] Filter(byte[] source,int width,int height,int radius,int mode,bool horizontal)
        {
            var result=new byte[source.Length]; int lines=horizontal?height:width,length=horizontal?width:height;
            var deque=new int[length+radius*2+1];
            for(int line=0;line<lines;line++)
            {
                int head=0,tail=0,sum=0;
                int Index(int p)=>horizontal?line*width+Mathf.Clamp(p,0,length-1):Mathf.Clamp(p,0,length-1)*width+line;
                for(int i=-radius;i<length+radius;i++)
                {
                    byte value=source[Index(i)];
                    if(mode==0) sum+=value;
                    else
                    {
                        while(tail>head && (mode>0?source[Index(deque[tail-1])]<=value:source[Index(deque[tail-1])]>=value)) tail--;
                        deque[tail++]=i;
                    }
                    int expired=i-radius*2-1;
                    if(expired>=-radius) { if(mode==0) sum-=source[Index(expired)];else if(tail>head && deque[head]<=expired) head++; }
                    int center=i-radius;
                    if(center>=0 && center<length) result[Index(center)]=mode==0?(byte)(sum/(radius*2+1)):source[Index(deque[head])];
                }
            }
            return result;
        }
    }
}
