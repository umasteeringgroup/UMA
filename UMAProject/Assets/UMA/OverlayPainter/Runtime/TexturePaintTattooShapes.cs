using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    public enum TexturePaintTattooDesign { TaperedLine, HookedLine, Spiral, TribalFlame, TribalScroll }
    public enum TexturePaintTattooSymmetry { None, MirrorAcrossPath, HalfTurn }
    public enum TexturePaintTattooCurlDirection { Inward, Outward, Clockwise, CounterClockwise, Alternating }

    /// <summary>Vector strokes rasterized once into a shared material coverage mask. Tapers change
    /// the contour, not the ink opacity. Distances are in full path-width units.</summary>
    public sealed class TexturePaintTattooShapeMask : IDisposable
    {
        public Texture2D Texture { get; private set; }
        private struct Point { public Vector2 p; public float radius; }
        private readonly int width, height;
        private readonly float aspect;
        private readonly float[] coverage;
        private readonly TexturePaintPathGeneratorSettings s;
        private sealed class ShapeStroke
        {
            public List<Point> points;
            public bool subtract;
        }
        private readonly List<ShapeStroke> tribalStrokes = new();
        private bool collectingTribal;
        public TexturePaintTattooShapeMask(TexturePaintPathGeneratorSettings settings)
        {
            s=settings.Clone();s.Normalize();aspect=s.tattooAspect;
            height=256;width=Mathf.Clamp(Mathf.CeilToInt(height*aspect),128,2048);
            coverage=new float[width*height];
            switch(s.tattooDesign)
            {
                case TexturePaintTattooDesign.Spiral:
                    Spiral(new Vector2(aspect*.5f,.5f),.39f,Mathf.PI*.1f,s.curlTurns+ .5f,s.lineWidth*.3f,1,true);break;
                case TexturePaintTattooDesign.TribalFlame: if(s.proceduralTribal)Tribal();else Flame();break;
                case TexturePaintTattooDesign.TribalScroll: if(s.proceduralTribal)Tribal();else Scroll();break;
                default: Line();break;
            }
            var pixels=new Color32[coverage.Length];
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(255,255,255,(byte)Mathf.RoundToInt(Mathf.Clamp01(coverage[i])*255));
            Texture=new Texture2D(width,height,TextureFormat.RGBA32,true,true)
            { name="Path Tattoo Shape",hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear };
            Texture.SetPixels32(pixels);Texture.Apply(true,false);
        }
        private Vector2 V(float x,float y)=>new Vector2(x*aspect,y);
        private Vector2 Mirror(Vector2 p)=>s.tattooMirror ? new Vector2(p.x,1-p.y) : p;
        private void Line(float thicknessScale=1)
        {
            float radius=s.lineWidth*.5f*thicknessScale;
            float curlWidth=Mathf.Min(radius,s.curlRadius*.3f);
            LineBounds(thicknessScale,out float start,out float end);
            var points=new List<Point>();
            for(int i=0;i<=96;i++)
            {
                float t=i/96f;
                float r=radius;
                if(s.curlStart)r*=Mathf.Lerp(curlWidth/radius,1,Mathf.SmoothStep(0,1,t/.25f));
                if(s.curlEnd)r*=Mathf.Lerp(curlWidth/radius,1,Mathf.SmoothStep(0,1,(1-t)/.25f));
                if(s.taperStart&&!s.curlStart)r*=Mathf.Pow(Mathf.Clamp01(t/s.taperStartLength),s.taperPower);
                if(s.taperEnd&&!s.curlEnd)r*=Mathf.Pow(Mathf.Clamp01((1-t)/s.taperEndLength),s.taperPower);
                float y=.5f+s.lineBend*.3f*Mathf.Sin(t*Mathf.PI*2);
                points.Add(new Point{p=V(Mathf.Lerp(start,end,t),y),radius=r});
            }
            Stroke(points);
            float slope=s.lineBend*.6f*Mathf.PI/((end-start)*aspect);
            if(s.curlStart)Curl(points[0].p,new Vector2(-1,-slope).normalized,curlWidth,s.curlStartSide,s.taperStart,s.taperStartLength);
            if(s.curlEnd)Curl(points[points.Count-1].p,new Vector2(1,slope).normalized,curlWidth,s.curlEndSide,s.taperEnd,s.taperEndLength);
        }
        private void LineBounds(float thicknessScale,out float start,out float end)
        {
            float curlWidth=Mathf.Min(s.lineWidth*.5f*thicknessScale,s.curlRadius*.3f);
            float inset=Mathf.Min(.35f,(s.curlRadius*1.5f+curlWidth)/aspect+.025f);
            start=s.curlStart?inset:.025f;end=s.curlEnd?1-inset:.975f;
        }
        private void Curl(Vector2 join,Vector2 outward,float thickness,float side,bool taper=true,float taperLength=1)
        {
            float sign=side<0?-1:1;
            var center=join+new Vector2(-outward.y,outward.x)*s.curlRadius*sign;
            var radial=join-center;
            Spiral(center,s.curlRadius,Mathf.Atan2(radial.y,radial.x),s.curlTurns,thickness,sign,false,taper,taperLength);
        }
        private void Spiral(Vector2 center,float radius,float angle,float turns,float thickness,float direction,bool pointedStart=false,bool pointedEnd=true,float taperLength=1)
        {
            var points=new List<Point>();
            int segments=Mathf.CeilToInt(80*turns);
            for(int i=0;i<=segments;i++)
            {
                float t=(float)i/segments,a=angle+direction*t*Mathf.PI*2*turns;
                float r=radius*Mathf.Pow(1-t,.8f);
                float stroke=thickness*(pointedEnd?Mathf.Pow(Mathf.Clamp01((1-t)/taperLength),s.taperPower*.55f):1);
                if(pointedStart)stroke*=Mathf.SmoothStep(0,1,t/.18f);
                points.Add(new Point{p=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,radius=stroke});
            }
            Stroke(points);
        }
        private void Blade(Vector2 a,Vector2 b,Vector2 c,Vector2 d,float thickness,bool subtract=false)
        {
            if(thickness<=0)return;
            var points=new List<Point>();
            for(int i=0;i<=64;i++)
            {
                float t=i/64f,q=1-t;
                float r=thickness*Mathf.Pow(Mathf.Sin(Mathf.PI*t),s.taperPower*.5f);
                points.Add(new Point{p=q*q*q*a+3*q*q*t*b+3*q*t*t*c+t*t*t*d,radius=r});
            }
            Stroke(points,subtract);
        }
        private void Flame()
        {
            float w=s.lineWidth;
            Blade(V(.025f,.82f),V(.33f,.72f),V(.35f,.02f),V(.79f,.45f),w*.26f);
            Blade(V(.18f,.91f),V(.53f,.87f),V(.35f,.39f),V(.62f,.37f),w*.18f);
            Blade(V(.27f,.11f),V(.24f,.55f),V(.70f,.90f),V(.975f,.18f),w*.27f);
            Blade(V(.57f,.86f),V(.93f,.91f),V(.74f,.43f),V(.90f,.07f),w*.17f);
            Spiral(V(.58f,.48f),.24f,-Mathf.PI*.1f,s.curlTurns,w*.16f,-1,true);
            Blade(V(.30f,.64f),V(.41f,.37f),V(.48f,.19f),V(.69f,.34f),w*s.negativeSpace,true);
            Ends(w*.24f);
        }
        private void Scroll()
        {
            float w=s.lineWidth;
            // Separate tapered blades and spiral loops leave deliberate negative-space channels.
            Blade(V(.025f,.86f),V(.24f,.68f),V(.18f,.19f),V(.40f,.28f),w*.26f);
            Blade(V(.14f,.86f),V(.32f,.76f),V(.19f,.48f),V(.32f,.48f),w*.16f);
            Blade(V(.975f,.14f),V(.76f,.32f),V(.84f,.78f),V(.60f,.72f),w*.26f);
            Blade(V(.85f,.12f),V(.68f,.24f),V(.81f,.52f),V(.68f,.52f),w*.16f);
            Spiral(V(.5f,.5f),.36f,-Mathf.PI*.65f,s.curlTurns+.5f,w*.26f,1,true);
            Spiral(V(.26f,.52f),.20f,Mathf.PI*.1f,s.curlTurns,w*.16f,1,true);
            Spiral(V(.74f,.49f),.20f,Mathf.PI*1.05f,s.curlTurns,w*.16f,1,true);
            Blade(V(.37f,.44f),V(.31f,.22f),V(.38f,.13f),V(.42f,.05f),w*.19f);
            Blade(V(.64f,.54f),V(.69f,.82f),V(.61f,.88f),V(.58f,.95f),w*.19f);
            Blade(V(.28f,.70f),V(.36f,.75f),V(.40f,.49f),V(.33f,.37f),w*s.negativeSpace,true);
            Blade(V(.73f,.30f),V(.65f,.25f),V(.61f,.51f),V(.68f,.63f),w*s.negativeSpace,true);
            Ends(w*.24f);
        }
        private void Ends(float thickness)
        {
            if(s.curlStart)Curl(V(.14f,.5f),Vector2.left,thickness,s.curlStartSide);
            if(s.curlEnd)Curl(V(.86f,.5f),Vector2.right,thickness,s.curlEndSide);
        }

        private void Tribal()
        {
            collectingTribal = true;
            Line(.45f);
            bool paired = s.tribalSymmetry != TexturePaintTattooSymmetry.None;
            int count = paired ? s.tribalArmCount / 2 : s.tribalArmCount;
            float spacing = (s.tribalBranchEnd - s.tribalBranchStart) / Mathf.Max(1, count - 1);
            for (int i = 0; i < count; i++)
            {
                float u = count == 1 ? (s.tribalBranchStart + s.tribalBranchEnd) * .5f :
                    Mathf.Lerp(s.tribalBranchStart, s.tribalBranchEnd, (float)i / (count - 1));
                u = Mathf.Clamp(u + Variation(i, 1) * spacing * .3f, s.tribalBranchStart, s.tribalBranchEnd);
                float side = paired || (i & 1) == 0 ? 1 : -1;
                TribalArm(u, side, i);
            }
            collectingTribal = false;
            FitTribalStrokes();
        }

        // A local integer hash keeps regeneration reproducible without consuming Unity's random state.
        private float Variation(int arm, uint salt)
        {
            unchecked
            {
                uint h = (uint)s.seed ^ ((uint)arm + 1) * 0x9e3779b9u ^ salt * 0x85ebca6bu;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return ((h & 0xffffffu) / 8388607.5f - 1) * s.tribalVariation;
            }
        }

        private float ArmCurl(float u, float side, int index)
        {
            float inward = u < .5f ? -side : side;
            return s.tribalCurlDirection switch
            {
                TexturePaintTattooCurlDirection.Outward => -inward,
                TexturePaintTattooCurlDirection.Clockwise => -1,
                TexturePaintTattooCurlDirection.CounterClockwise => 1,
                TexturePaintTattooCurlDirection.Alternating => (index & 1) == 0 ? 1 : -1,
                _ => inward
            };
        }

        private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float q = 1 - t;
            return q*q*q*a + 3*q*q*t*b + 3*q*t*t*c + t*t*t*d;
        }

        private void TribalArm(float u, float side, int index)
        {
            bool scroll = s.tattooDesign == TexturePaintTattooDesign.TribalScroll;
            float direction = ArmCurl(u, side, index);
            float length = s.tribalArmLength * (1 + Variation(index, 2) * .35f);
            float angle = (s.tribalArmAngle + Variation(index, 3) * 22) * Mathf.Deg2Rad;
            float forward = u < .5f ? -1 : 1;
            var along = new Vector2(forward * Mathf.Cos(angle), side * Mathf.Sin(angle));
            var across = new Vector2(-along.y, along.x);
            float width = s.lineWidth * .22f * (1 + Variation(index, 4) * .25f);
            float sweep = s.tribalArmSweep + Variation(index, 5) * .3f;
            LineBounds(.45f,out float start,out float end);
            var root = V(Mathf.Lerp(start,end,u), .5f + s.lineBend * .3f * Mathf.Sin(u * Mathf.PI * 2));
            var tip = root + along * length;
            var b = root + along * length * .35f;
            var c = tip - along * length * .25f + across * length * sweep * direction * (scroll ? .5f : s.curlTurns * .6f);
            float curlRadius = Mathf.Min(s.curlRadius, length * .4f);
            float joinWidth = Mathf.Min(width * .65f, curlRadius * .28f);
            var points = new List<Point>();
            for (int i = 0; i <= 64; i++)
            {
                float t = i / 64f;
                float radius = scroll ? Mathf.Lerp(width, joinWidth, t) * (.8f + .4f * Mathf.Sin(t * Mathf.PI)) :
                    width * (.8f + .6f * Mathf.Sin(t * Mathf.PI)) * Mathf.Pow(1 - t, s.taperPower * .55f);
                points.Add(new Point { p = Bezier(root, b, c, tip, t), radius = radius });
            }
            Stroke(points);
            if (scroll)
            {
                var tangent = (tip - c).normalized;
                var center = tip + new Vector2(-tangent.y, tangent.x) * curlRadius * direction;
                var radial = tip - center;
                Spiral(center, curlRadius, Mathf.Atan2(radial.y, radial.x),
                    s.curlTurns * (1 + Variation(index, 6) * .2f), joinWidth * .8f, direction);
            }
            if (s.negativeSpace > 0)
            {
                var cut = new List<Point>();
                for (int i = 0; i <= 48; i++)
                {
                    float t = i / 48f, at = Mathf.Lerp(.12f, .82f, t);
                    cut.Add(new Point { p = Bezier(root, b, c, tip, at) + across * width * .35f * direction,
                        radius = s.negativeSpace * s.lineWidth * .5f * Mathf.Sin(t * Mathf.PI) });
                }
                Stroke(cut, true);
            }
        }

        private void CollectTribalStroke(List<Point> points, bool subtract)
        {
            tribalStrokes.Add(new ShapeStroke { points = points, subtract = subtract });
            if (s.tribalSymmetry == TexturePaintTattooSymmetry.None) return;
            var reflected = new List<Point>(points.Count);
            foreach (var point in points)
                reflected.Add(new Point { radius = point.radius, p = new Vector2(
                    s.tribalSymmetry == TexturePaintTattooSymmetry.HalfTurn ? aspect - point.p.x : point.p.x,
                    1 - point.p.y) });
            tribalStrokes.Add(new ShapeStroke { points = reflected, subtract = subtract });
        }

        private void FitTribalStrokes()
        {
            // Fit the complete silhouette, including curls and stroke thickness, before rasterizing.
            // This preserves proportions and a transparent gutter even for long or widely swept arms.
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            foreach (var stroke in tribalStrokes)
                foreach (var point in stroke.points)
                {
                    var pad = Vector2.one * point.radius;
                    min = Vector2.Min(min, point.p - pad); max = Vector2.Max(max, point.p + pad);
                }
            Vector2 center = (min + max) * .5f, size = max - min;
            float scale = Mathf.Min(1, (aspect - .04f) / Mathf.Max(.001f, size.x), .96f / Mathf.Max(.001f, size.y));
            foreach (var stroke in tribalStrokes)
                for (int i = 0; i < stroke.points.Count; i++)
                {
                    var point = stroke.points[i];
                    point.p = (point.p - center) * scale + V(.5f, .5f); point.radius *= scale;
                    stroke.points[i] = point;
                }
            // Union every ink stroke first, then carve the shared negative spaces, independent of arm order.
            foreach (var stroke in tribalStrokes) if (!stroke.subtract) Stroke(stroke.points);
            foreach (var stroke in tribalStrokes) if (stroke.subtract) Stroke(stroke.points, true);
            tribalStrokes.Clear();
        }

        private void Stroke(List<Point> points,bool subtract=false)
        {
            if (collectingTribal) { CollectTribalStroke(points, subtract); return; }
            // Local bounding boxes avoid scanning the entire image for every curve segment.
            float aa=Mathf.Max(aspect/width,1f/height);
            var stroke=subtract?new float[coverage.Length]:coverage;
            for(int i=1;i<points.Count;i++)
            {
                var a=Mirror(points[i-1].p);var b=Mirror(points[i].p);
                float ra=points[i-1].radius,rb=points[i].radius,pad=Mathf.Max(ra,rb)+aa;
                int x0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x,b.x)-pad)/aspect*width),0,width-1);
                int x1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x,b.x)+pad)/aspect*width),0,width-1);
                int y0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y,b.y)-pad)*height),0,height-1);
                int y1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.y,b.y)+pad)*height),0,height-1);
                Vector2 delta=b-a;float inverse=1/Mathf.Max(delta.sqrMagnitude,1e-10f);
                for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {
                    Vector2 p=new Vector2((x+.5f)/width*aspect,(y+.5f)/height);
                    float t=Mathf.Clamp01(Vector2.Dot(p-a,delta)*inverse);
                    float distance=(p-a-delta*t).magnitude,r=Mathf.Lerp(ra,rb,t);
                    float alpha=Mathf.Clamp01((r-distance)/aa+.5f);
                    int index=y*width+x;if(alpha>stroke[index])stroke[index]=alpha;
                }
            }
            if(subtract)for(int i=0;i<coverage.Length;i++)coverage[i]*=1-stroke[i];
        }
        public void Dispose(){if(Texture!=null)UnityEngine.Object.DestroyImmediate(Texture);Texture=null;}
    }
}
