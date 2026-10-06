using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    public enum TexturePaintPathWoundType { HealedScar, FreshCut, Burn, StretchMark }
    /// <summary>Saved on the path, never on its raster cache. Additional generators own extensionJson.</summary>
    [Serializable]
    public sealed class TexturePaintPathGeneratorSettings
    {
        // Unity inline serialization can recreate null classes as default objects. Missing data
        // must never activate a generator on an older image, seam, or garment path.
        public bool enabled;
        public string generatorId;
        public string extensionJson = "{}";
        public string text = "Tattoo";
        public Font font;
        public FontStyle fontStyle;
        public bool preserveTextAspect = true;
        public bool textFlipX, textFlipY;
        public int textRepeats = 1;
        public float textInset = .05f;
        public TexturePaintTattooDesign tattooDesign;
        public float tattooAspect=2.5f, lineWidth=.28f, lineBend=.25f;
        public bool taperStart=true, taperEnd=true, curlStart, curlEnd, tattooMirror;
        public float taperStartLength=.4f,taperEndLength=.4f,taperPower=1.4f;
        public float curlRadius=.17f,curlTurns=.9f,curlStartSide=1,curlEndSide=-1,negativeSpace=.065f;
        public int tattooRepeats=1;
        // Disabled in older saved settings so their fixed tribal silhouettes remain unchanged.
        public bool proceduralTribal;
        public int tribalArmCount = 4;
        public float tribalBranchStart = .18f, tribalBranchEnd = .82f;
        public float tribalArmLength = .7f, tribalArmAngle = 55f, tribalArmSweep = .45f;
        public float tribalVariation = .15f;
        public TexturePaintTattooSymmetry tribalSymmetry;
        public TexturePaintTattooCurlDirection tribalCurlDirection;
        public Color color = new Color(.035f, .025f, .045f, 1);
        public Color healedColor = new Color(.62f, .36f, .32f, 1);
        public Color rimColor = new Color(.72f, .19f, .16f, 1);
        public Color threadColor = new Color(.065f, .055f, .045f, 1);
        public float healing = .25f, opening = .35f, taper = .65f, irregularity = .2f;
        public TexturePaintPathWoundType woundType = TexturePaintPathWoundType.FreshCut;
        public float depth = .12f, edgeLift = .07f, inflammation = .25f;
        public float roughness = .5f, relief = .015f, normalStrength = 1;
        public bool stitches, removedStitches;
        public int stitchCount = 13, seed = 17;
        public float stitchThickness = .012f, stitchSpan = .65f, stitchAngle = 12f, puckering = .25f;
        public TexturePaintHemSeamSettings seam;
        public TexturePaintGarmentSettings garment;

        public TexturePaintPathGeneratorSettings Clone()
        {
            var copy = (TexturePaintPathGeneratorSettings)MemberwiseClone();
            copy.seam = seam?.enabled == true ? seam.Clone() : null;
            copy.garment = garment?.enabled == true ? garment.Clone() : null;
            return copy;
        }
        public void Normalize()
        {
            float F(float v, float a, float b, float d) => float.IsFinite(v) ? Mathf.Clamp(v,a,b) : d;
            if(string.IsNullOrEmpty(generatorId))enabled=false;
            if(seam?.enabled!=true)seam=null;
            if(garment?.enabled!=true)garment=null;
            Color C(Color c) => new Color(F(c.r,0,1,0),F(c.g,0,1,0),F(c.b,0,1,0),F(c.a,0,1,1));
            color=C(color);rimColor=C(rimColor);healedColor=C(healedColor);threadColor=C(threadColor);
            if(!Enum.IsDefined(typeof(FontStyle),fontStyle))fontStyle=FontStyle.Normal;
            if(!Enum.IsDefined(typeof(TexturePaintPathWoundType),woundType))woundType=TexturePaintPathWoundType.FreshCut;
            if(!Enum.IsDefined(typeof(TexturePaintTattooDesign),tattooDesign))tattooDesign=TexturePaintTattooDesign.TaperedLine;
            tattooAspect=F(tattooAspect,1,8,2.5f);lineWidth=F(lineWidth,.01f,.65f,.28f);lineBend=F(lineBend,-1,1,.25f);
            taperStartLength=F(taperStartLength,.01f,1,.4f);taperEndLength=F(taperEndLength,.01f,1,.4f);taperPower=F(taperPower,.25f,4,1.4f);
            curlRadius=F(curlRadius,.03f,.22f,.17f);curlTurns=F(curlTurns,.15f,1.8f,.9f);
            curlStartSide=curlStartSide<0?-1:1;curlEndSide=curlEndSide<0?-1:1;negativeSpace=F(negativeSpace,0,.2f,.065f);
            tattooRepeats=Mathf.Clamp(tattooRepeats,1,32);
            if(!Enum.IsDefined(typeof(TexturePaintTattooSymmetry),tribalSymmetry))tribalSymmetry=TexturePaintTattooSymmetry.None;
            if(!Enum.IsDefined(typeof(TexturePaintTattooCurlDirection),tribalCurlDirection))tribalCurlDirection=TexturePaintTattooCurlDirection.Inward;
            tribalArmCount=Mathf.Clamp(tribalArmCount,0,16);
            if(tribalSymmetry!=TexturePaintTattooSymmetry.None && (tribalArmCount&1)!=0)tribalArmCount++;
            tribalBranchStart=F(tribalBranchStart,.05f,.95f,.18f);tribalBranchEnd=F(tribalBranchEnd,.05f,.95f,.82f);
            if(tribalBranchStart>tribalBranchEnd)(tribalBranchStart,tribalBranchEnd)=(tribalBranchEnd,tribalBranchStart);
            tribalArmLength=F(tribalArmLength,.1f,1.5f,.7f);tribalArmAngle=F(tribalArmAngle,10,160,55);
            tribalArmSweep=F(tribalArmSweep,-1,1,.45f);tribalVariation=F(tribalVariation,0,1,.15f);
            text ??= string.Empty; if (text.Length > 512) text = text.Substring(0,512);
            textRepeats = Mathf.Clamp(textRepeats,1,64); textInset = F(textInset,0,.45f,.05f);
            healing=F(healing,0,1,.25f); opening=F(opening,.005f,.8f,.35f); taper=F(taper,0,1,.65f);
            irregularity=F(irregularity,0,1,.2f); depth=F(depth,0,.4f,.12f); edgeLift=F(edgeLift,0,.3f,.07f);
            inflammation=F(inflammation,0,1,.25f); roughness=F(roughness,0,1,.5f);
            relief=F(relief,-.4f,.4f,.015f); normalStrength=F(normalStrength,0,4,1);
            stitchCount=Mathf.Clamp(stitchCount,1,256); stitchThickness=F(stitchThickness,.002f,.08f,.012f);
            stitchSpan=F(stitchSpan,.05f,.9f,.65f); stitchAngle=F(stitchAngle,-75,75,12);
            puckering=F(puckering,0,1,.25f); seam?.Normalize(); garment?.Normalize();
        }
    }

    /// <summary>Main-thread preparation is called once per rebuild. Bind is called for each output.
    /// Resources must be disposable; they are not serialized or retained by the painting engine.</summary>
    public interface ITexturePaintPathGenerator
    {
        string Id { get; }
        string MenuPath { get; }
        TexturePaintPathGeneratorSettings CreateSettings();
        IDisposable Prepare(TexturePaintPathGeneratorSettings settings);
        void Bind(MaterialPropertyBlock properties, TexturePaintPathGeneratorSettings settings,
            IDisposable prepared, TexturePaintChannel channel, Color channelColor, Vector2 size);
    }

    /// <summary>Optional output for third-party generators. X runs along the complete path and Y
    /// across it. Every channel uses Coverage alpha, including channels without a supplied image.
    /// Images contain straight color in Unity's declared texture color space; data maps are linear.
    /// Normal Control is preferred over baked normals so relief follows curved and mirrored UVs.</summary>
    public sealed class TexturePaintPathGeneratorRaster : IDisposable
    {
        public readonly Dictionary<TexturePaintChannel,Texture2D> Channels = new();
        public Texture2D Coverage;
        public bool OwnsTextures = true;
        public void Bind(MaterialPropertyBlock properties,TexturePaintChannel channel,Color fallback)
        {
            if(Coverage==null)throw new InvalidOperationException("A path generator raster requires shared RGBA coverage.");
            properties.SetInt("_PathGeneratorEnabled",5);
            properties.SetTexture("_PathRasterCoverage",Coverage);
            bool has=Channels.TryGetValue(channel,out var texture)&&texture!=null;
            properties.SetTexture("_PathRaster",has?texture:Texture2D.whiteTexture);
            properties.SetColor("_PathRasterColor",has?Color.white:TexturePaintChannelUtility.WorkingColor(channel,fallback));
        }
        public void Dispose()
        {
            if(OwnsTextures)
            {
                var textures=new HashSet<Texture2D>(Channels.Values);textures.Add(Coverage);
                foreach(var texture in textures)if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);
            }
            Channels.Clear();Coverage=null;
        }
    }

    public static class TexturePaintPathGenerators
    {
        public const string ScarId = "com.uma.path.scar-wound";
        // Keep the original lettering ID so already-created text paths retain their content.
        public const string TextId = "com.uma.path.tattoo";
        public const string TattooId = "com.uma.path.tattoo-shape";
        private static readonly List<ITexturePaintPathGenerator> generators = CreateBuiltins();
        public static IReadOnlyList<ITexturePaintPathGenerator> All => generators.AsReadOnly();
        public static ITexturePaintPathGenerator Find(string id) => generators.Find(x => x.Id == id);
        public static bool IsTattoo(string id) => id==TattooId || id?.StartsWith(TattooId+".",StringComparison.Ordinal)==true;
        public static void Register(ITexturePaintPathGenerator generator)
        {
            if (generator == null || string.IsNullOrWhiteSpace(generator.Id) || string.IsNullOrWhiteSpace(generator.MenuPath))
                throw new ArgumentException("A path generator needs a stable ID and menu path.");
            generators.RemoveAll(x=>x.Id==generator.Id); generators.Add(generator);
        }
        public static bool Unregister(string id) => generators.RemoveAll(x=>x.Id==id)>0;
        public static bool IsLinearGarment(TexturePaintGarmentPreset preset) =>
            preset == TexturePaintGarmentPreset.MetalZipper || preset == TexturePaintGarmentPreset.CoilZipper ||
            preset == TexturePaintGarmentPreset.FrayedTear || preset == TexturePaintGarmentPreset.ExposedThreads;
        private static List<ITexturePaintPathGenerator> CreateBuiltins()
        {
            var result = new List<ITexturePaintPathGenerator>
            {
                new Builtin(ScarId,"Skin/Scar and Wound",1), new Builtin(TextId,"Text and Lettering",2),
                new Builtin(TattooId,"Tattoo/Curved Line",6),
                new Builtin(TattooId+".hooks","Tattoo/Inward Hooks",6,()=>new TexturePaintPathGeneratorSettings
                    {tattooDesign=TexturePaintTattooDesign.HookedLine,curlStart=true,curlEnd=true,lineBend=.4f,lineWidth=.12f,curlRadius=.19f}),
                new Builtin(TattooId+".spiral","Tattoo/Spiral",6,()=>new TexturePaintPathGeneratorSettings
                    {tattooDesign=TexturePaintTattooDesign.Spiral,tattooAspect=1}),
                new Builtin(TattooId+".flame","Tattoo/Tribal Flame",6,()=>new TexturePaintPathGeneratorSettings
                    {tattooDesign=TexturePaintTattooDesign.TribalFlame,proceduralTribal=true}),
                new Builtin(TattooId+".scroll","Tattoo/Tribal Scroll",6,()=>new TexturePaintPathGeneratorSettings
                    {tattooDesign=TexturePaintTattooDesign.TribalScroll,proceduralTribal=true,tribalSymmetry=TexturePaintTattooSymmetry.HalfTurn})
            };
            foreach (TexturePaintStitchPattern pattern in Enum.GetValues(typeof(TexturePaintStitchPattern)))
            {
                var p=pattern;
                result.Add(new Builtin("com.uma.path.stitch."+p,"Stitching/"+p,3,()=>
                {
                    var s=TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.Topstitch);
                    s.rows[0].pattern=p; s.rows[0].span=.4f;
                    return new TexturePaintPathGeneratorSettings { seam=s };
                }));
            }
            foreach (TexturePaintSeamPreset preset in Enum.GetValues(typeof(TexturePaintSeamPreset)))
            {
                var p=preset;
                result.Add(new Builtin("com.uma.path.seam."+p,"Seams and Trim/"+p,3,
                    ()=>new TexturePaintPathGeneratorSettings { seam=TexturePaintHemSeamSettings.Create(p) }));
            }
            foreach (TexturePaintGarmentPreset preset in Enum.GetValues(typeof(TexturePaintGarmentPreset)))
            {
                if (!IsLinearGarment(preset)) continue;
                var p=preset;
                result.Add(new Builtin("com.uma.path.garment."+p,"Fasteners and Distress/"+p,4,
                    ()=>new TexturePaintPathGeneratorSettings { garment=TexturePaintGarmentSettings.Create(p) }));
            }
            return result;
        }
        private sealed class Builtin : ITexturePaintPathGenerator
        {
            public string Id { get; }
            public string MenuPath { get; }
            private readonly int kind;
            private readonly Func<TexturePaintPathGeneratorSettings> factory;
            public Builtin(string id,string menu,int kind,Func<TexturePaintPathGeneratorSettings> factory=null)
            { Id=id;MenuPath=System.Text.RegularExpressions.Regex.Replace(menu,"([a-z])([A-Z])","$1 $2");this.kind=kind;this.factory=factory; }
            public TexturePaintPathGeneratorSettings CreateSettings()
            {
                var s=factory?.Invoke() ?? new TexturePaintPathGeneratorSettings();s.generatorId=Id;s.enabled=true;
                if(kind==1) { s.color=new Color(.25f,.018f,.025f,1);s.relief=0; }
                return s;
            }
            public IDisposable Prepare(TexturePaintPathGeneratorSettings settings) => kind==6 ?
                new TexturePaintTattooShapeMask(settings) : kind==2 ? new TexturePaintPathTextMask(settings.text,settings.font,settings.fontStyle) : null;
            public void Bind(MaterialPropertyBlock p,TexturePaintPathGeneratorSettings s,IDisposable prepared,
                TexturePaintChannel channel,Color channelColor,Vector2 size)
            {
                p.SetInt("_PathGeneratorEnabled",kind); p.SetInt("_PathChannel",(int)channel);
                p.SetInt("_PathWoundType",(int)s.woundType);
                p.SetColor("_PathChannelColor",TexturePaintChannelUtility.WorkingColor(channel,channelColor));
                p.SetColor("_PathColor",TexturePaintChannelUtility.WorkingColor(TexturePaintChannel.Albedo,s.color));
                p.SetColor("_PathHealedColor",TexturePaintChannelUtility.WorkingColor(TexturePaintChannel.Albedo,s.healedColor));
                p.SetColor("_PathRimColor",TexturePaintChannelUtility.WorkingColor(TexturePaintChannel.Albedo,s.rimColor));
                p.SetColor("_PathThreadColor",TexturePaintChannelUtility.WorkingColor(TexturePaintChannel.Albedo,s.threadColor));
                p.SetVector("_PathShape",new Vector4(s.healing,s.opening,s.taper,s.irregularity));
                p.SetVector("_PathRelief",new Vector4(s.depth,s.edgeLift,s.inflammation,s.relief));
                p.SetVector("_PathFinish",new Vector4(s.roughness,s.normalStrength,s.seed,s.puckering));
                p.SetVector("_PathStitches",new Vector4(s.stitches ? (s.removedStitches?2:1):0,s.stitchCount,s.stitchThickness,s.stitchSpan));
                p.SetFloat("_PathStitchSlant",Mathf.Tan(s.stitchAngle*Mathf.Deg2Rad));
                if (prepared is TexturePaintPathTextMask text)
                {
                    p.SetTexture("_PathText",text.Texture);
                    float fitX=1,fitY=1;
                    float regionAspect=Mathf.Max(.001f,size.y/Mathf.Max(.0001f,size.x)/s.textRepeats);
                    float aspect=(float)text.Texture.width/text.Texture.height;
                    if(s.preserveTextAspect) { if(aspect<regionAspect)fitX=aspect/regionAspect;else fitY=regionAspect/aspect; }
                    // Signed scale mirrors glyph coordinates and their sampling derivatives together.
                    // The shared mask also flips every material output and its generated relief.
                    if(s.textFlipX)fitX=-fitX;
                    if(s.textFlipY)fitY=-fitY;
                    p.SetVector("_PathTextLayout",new Vector4(fitX*(1-2*s.textInset),fitY*(1-2*s.textInset),s.textRepeats,0));
                }
                if(prepared is TexturePaintTattooShapeMask shape)
                {
                    p.SetTexture("_PathText",shape.Texture);
                    p.SetVector("_PathTextLayout",new Vector4(1,1,s.tattooRepeats,0));
                }
                s.seam?.Bind(p,channel); s.garment?.Bind(p,channel,size);
            }
        }
    }
}
