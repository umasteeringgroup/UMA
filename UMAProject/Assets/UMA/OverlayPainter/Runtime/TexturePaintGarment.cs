using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    public enum TexturePaintZipperToothShape { ConstructionDefault, Interlocking, Rounded, Bars, Molded, Chevron, Coil }
    public enum TexturePaintZipperTapeWeave { Original, Plain, Twill, Herringbone, Ribbed }
    public enum TexturePaintZipperStitchStyle { Straight, Zigzag, Chain }
    public enum TexturePaintZipperStylePreset { SilverInterlock, AntiqueBrass, GoldCoil, BlackMolded, BronzeRounded, SilverBars, GoldChevron }

    [Serializable]
    public sealed class TexturePaintZipperStyleSettings
    {
        public TexturePaintZipperStylePreset preset;
        public TexturePaintZipperToothShape toothShape;
        public float toothWidth=1, toothLength=1, toothRoundness=1, bankOffset=.5f;
        public float tapeWidth=.72f, tapeHeight=.12f, weaveScale, weaveContrast=.075f, weaveHeight=.025f;
        public TexturePaintZipperTapeWeave weave;
        public float cordWidth=.018f, cordHeight, cordInset=.018f;
        public Color cordColor=new Color(.2f,.2f,.2f,1);
        public TexturePaintZipperStitchStyle stitchStyle;
        public float stitchLength=.64f, stitchSpread=.025f, stitchHeight=1, stitchRoughness=.66f;
        public TexturePaintZipperStyleSettings Clone() => (TexturePaintZipperStyleSettings)MemberwiseClone();
        private static float F(float v,float min,float max,float fallback)=>float.IsFinite(v)?Mathf.Clamp(v,min,max):fallback;
        public void Normalize()
        {
            if(!Enum.IsDefined(typeof(TexturePaintZipperToothShape),toothShape))toothShape=0;
            if(!Enum.IsDefined(typeof(TexturePaintZipperTapeWeave),weave))weave=0;
            if(!Enum.IsDefined(typeof(TexturePaintZipperStitchStyle),stitchStyle))stitchStyle=0;
            if(!Enum.IsDefined(typeof(TexturePaintZipperStylePreset),preset))preset=0;
            toothWidth=F(toothWidth,.4f,2,1);toothLength=F(toothLength,.3f,1.4f,1);toothRoundness=F(toothRoundness,0,2,1);bankOffset=F(bankOffset,0,1,.5f);
            tapeWidth=F(tapeWidth,.1f,.98f,.72f);tapeHeight=F(tapeHeight,0,2,.12f);
            weaveScale=F(weaveScale,0,.1f,0);weaveContrast=F(weaveContrast,0,1,.075f);weaveHeight=F(weaveHeight,0,1,.025f);
            cordWidth=F(cordWidth,.002f,.1f,.018f);cordHeight=F(cordHeight,0,3,0);cordInset=F(cordInset,0,.2f,.018f);
            cordColor=new Color(F(cordColor.r,0,1,.2f),F(cordColor.g,0,1,.2f),F(cordColor.b,0,1,.2f),F(cordColor.a,0,1,1));
            stitchLength=F(stitchLength,.1f,.95f,.64f);stitchSpread=F(stitchSpread,.002f,.1f,.025f);
            stitchHeight=F(stitchHeight,0,4,1);stitchRoughness=F(stitchRoughness,0,1,.66f);
        }
    }

    public enum TexturePaintGarmentKind { Wrinkles, Wear, Pockets, Hardware, Distress, Labels, Wraps, Welds, Lace }
    public enum TexturePaintGarmentPreset
    {
        TensionFolds, CompressionFolds, ElbowKneeFolds, CuffGather, Pleats,
        ThighFade, HipWhiskers, KneeHoneycombs, PocketWear, DirtyCuffs,
        PatchPocket, WeltPocket, PocketFlap, Waistband, ReinforcementPanel,
        MetalZipper, CoilZipper, Buttonhole, SewnButton, Snap, Rivet, Eyelet,
        Abrasion, ExposedThreads, FrayedTear, DarnedRepair, RepairPatch,
        WovenLabel, LeatherPatch, PrintedLogo, RubberBadge, EmbroideredPatch,
        LeatherWrap, ClothBandage, GripTape, GauzeWrap, AdhesiveDressing,
        TigWeld, MigWeld, SolderSeam, BrazedSeam,
        FloralLace, GothicLace, ScallopedLace
    }

    [Serializable]
    public sealed class TexturePaintZipperPartSettings
    {
        public float height=1;
        public float metallic=-1, smoothness=-1;
        public TexturePaintZipperPartSettings Clone() => (TexturePaintZipperPartSettings)MemberwiseClone();
        public void Normalize()
        {
            height=float.IsFinite(height)?Mathf.Clamp(height,0,10):1;
            metallic=float.IsFinite(metallic)?Mathf.Clamp(metallic,-1,1):-1;
            smoothness=float.IsFinite(smoothness)?Mathf.Clamp(smoothness,-1,1):-1;
        }
        public Vector4 ShaderValue(float defaultMetallic,float defaultRoughness) => new Vector4(
            height,metallic<0?defaultMetallic:metallic,smoothness<0?defaultRoughness:1-smoothness,0);
    }

    /// <summary>Shared procedural source for native path and projection layers. All distances
    /// except normalized placement parameters are world units (UV units on 2D paths).</summary>
    [Serializable]
    public sealed class TexturePaintGarmentSettings
    {
        public bool enabled;
        public TexturePaintGarmentPreset preset;
        public TexturePaintGarmentKind kind;
        public int variant;
        public int seed=17;
        public float detailSize=.014f;
        public float relief=.0015f;
        public float normalStrength=1;
        public float amount=1;
        // Append-only serialized controls shared by path and projection wraps.
        public float wrapOverlap=.25f, wrapAngle=25, wrapWrinkles=.2f, wrapLift=.2f;
        public float wrapFray=.15f, wrapStain=.1f, wrapResidue=.1f, wrapWeave=.001f;
        public float weldWidth=.22f, weldRipple=.45f, weldHeat=.65f, weldHeatWidth=.25f, weldSoot=.1f, weldSpatter=.15f;
        public float laceThread=.012f, laceScallop=.14f, laceNet=.5f, laceTwist=.3f;
        public float angle;
        public float irregularity=.35f;
        public float falloff=.15f;
        public float taper=.4f;
        public Vector2 focus=new Vector2(.5f,.05f);
        public float spread=.65f;
        public float wear=.3f, recess=.3f, dirt=.2f, protection=.85f;
        public float edgeWidth=.035f;
        // False retains the finite end construction in existing documents and presets.
        // Opening an end continues a strip through the boundary instead of closing it.
        public bool openStart, openEnd;
        public float stitchSize=.00035f, stitchSpacing=.0035f;
        public int stitchRows=2;
        public Color threadColor=new Color(.7f,.38f,.1f,1);
        public Color surfaceColor=new Color(.13f,.22f,.3f,1);
        public Color accentColor=new Color(.55f,.31f,.13f,1);
        public float colorAmount;
        public float roughness=.8f, metalRoughness=.32f;
        public float opening=.08f, slider=.82f;
        // Hardware size relative to the ribbon; tape and stitch placement stay independent.
        public float zipperWidth=1;
        public TexturePaintZipperStyleSettings zipperStyle=new();
        public TexturePaintZipperPartSettings zipperTeeth=new();
        public TexturePaintZipperPartSettings zipperStops=new(){height=2.5f};
        public TexturePaintZipperPartSettings zipperSlider=new(){height=1.5f};
        public TexturePaintZipperPartSettings zipperPull=new(){height=2};
        public TexturePaintZipperPartSettings zipperTang=new(){height=2};
        // Keep the original serialized name so existing documents, presets and
        // callers retain their authored zipper depth, including an intentional zero.
        [HideInInspector]
        public float zipperDepth=1;
        [SerializeField] private int zipperDepthVersion=1;
        public float depthStrength { get=>zipperDepth; set=>zipperDepth=value; }
        public float shadingStrength=1;
        [SerializeField] private int shadingStrengthVersion=1;
        public int repeat=1;
        public float damage=.45f, fray=.55f;
        public Texture2D motif;
        public Sprite motifSprite;
        public float motifScale=.75f;
        public bool motifColor=true;
        // Height is merged with the existing normal through the Normal Control RNM pass.
        // Keep direct normal output opt-in; ordinary layer blending would replace fabric detail.
        public bool albedo=true, normal, ao=true, roughnessOutput=true, metallic=true;
        public bool height=true, masks;
        public TexturePaintLayerReference foldInput, protectionInput;
        public bool HasReferences => enabled && Kind(preset)==TexturePaintGarmentKind.Wear && (foldInput?.IsSet==true || protectionInput?.IsSet==true);
        public bool SupportsOpenEnds => preset==TexturePaintGarmentPreset.Waistband ||
            preset==TexturePaintGarmentPreset.ReinforcementPanel || preset==TexturePaintGarmentPreset.RepairPatch ||
            preset==TexturePaintGarmentPreset.MetalZipper || preset==TexturePaintGarmentPreset.CoilZipper ||
            Kind(preset)==TexturePaintGarmentKind.Labels && preset!=TexturePaintGarmentPreset.PrintedLogo;
        public TexturePaintGarmentSettings Clone()
        {
            var copy=(TexturePaintGarmentSettings)MemberwiseClone();
            copy.zipperStyle=zipperStyle?.Clone();
            copy.zipperTeeth=zipperTeeth?.Clone();copy.zipperStops=zipperStops?.Clone();
            copy.zipperSlider=zipperSlider?.Clone();copy.zipperPull=zipperPull?.Clone();copy.zipperTang=zipperTang?.Clone();
            copy.foldInput=foldInput?.Clone();copy.protectionInput=protectionInput?.Clone();return copy;
        }
        private static float F(float value,float min,float max,float fallback)
            => float.IsFinite(value) ? Mathf.Clamp(value,min,max) : fallback;
        private static Color C(Color value,Color fallback)=>new Color(F(value.r,0,1,fallback.r),F(value.g,0,1,fallback.g),F(value.b,0,1,fallback.b),F(value.a,0,1,fallback.a));
        public void Normalize()
        {
            if(zipperDepthVersion<1){zipperDepth=1;zipperDepthVersion=1;}
            if(shadingStrengthVersion<1){shadingStrength=1;shadingStrengthVersion=1;}
            if(!Enum.IsDefined(typeof(TexturePaintGarmentPreset),preset))preset=TexturePaintGarmentPreset.TensionFolds;
            kind=Kind(preset);variant=Variant(preset);
            detailSize=F(detailSize,.0001f,10,.014f);relief=F(relief,0,.1f,.0015f);
            normalStrength=F(normalStrength,0,8,1);amount=F(amount,0,1,1);angle=F(angle,-180,180,0);
            irregularity=F(irregularity,0,1,.35f);falloff=F(falloff,.001f,.49f,.15f);taper=F(taper,0,1,.4f);
            wrapOverlap=F(wrapOverlap,0,.85f,.25f);wrapAngle=F(wrapAngle,-75,75,25);
            wrapWrinkles=F(wrapWrinkles,0,1,.2f);wrapLift=F(wrapLift,0,2,.2f);
            wrapFray=F(wrapFray,0,1,.15f);wrapStain=F(wrapStain,0,1,.1f);
            wrapResidue=F(wrapResidue,0,1,.1f);wrapWeave=F(wrapWeave,.0001f,.02f,.001f);
            weldWidth=F(weldWidth,.025f,.65f,.22f);weldRipple=F(weldRipple,0,1,.45f);
            weldHeat=F(weldHeat,0,1,.65f);weldHeatWidth=F(weldHeatWidth,.01f,.45f,.25f);
            weldSoot=F(weldSoot,0,1,.1f);weldSpatter=F(weldSpatter,0,1,.15f);
            laceThread=F(laceThread,.002f,.06f,.012f);laceScallop=F(laceScallop,0,.25f,.14f);
            laceNet=F(laceNet,0,1,.5f);laceTwist=F(laceTwist,0,1,.3f);
            focus=new Vector2(F(focus.x,-1,2,.5f),F(focus.y,-1,2,.05f));spread=F(spread,.05f,2,.65f);
            wear=F(wear,0,1,.3f);recess=F(recess,0,1,.3f);dirt=F(dirt,0,1,.2f);protection=F(protection,0,1,.85f);
            edgeWidth=F(edgeWidth,.005f,.2f,.035f);stitchSize=F(stitchSize,.00005f,.02f,.00035f);
            stitchSpacing=F(stitchSpacing,.0001f,.1f,.0035f);stitchRows=Mathf.Clamp(stitchRows,0,3);
            roughness=F(roughness,0,1,.8f);metalRoughness=F(metalRoughness,0,1,.32f);
            opening=F(opening,0,.7f,.08f);slider=F(slider,0,1,.82f);repeat=Mathf.Clamp(repeat,1,32);
            zipperWidth=zipperWidth<=0 ? 1 : F(zipperWidth,.1f,2,1);
            zipperDepth=F(zipperDepth,0,4,1);
            zipperStyle??=new();zipperStyle.Normalize();
            zipperTeeth??=new();zipperStops??=new(){height=2.5f};zipperSlider??=new(){height=1.5f};
            zipperPull??=new(){height=2};zipperTang??=new(){height=2};
            zipperTeeth.Normalize();zipperStops.Normalize();zipperSlider.Normalize();zipperPull.Normalize();zipperTang.Normalize();
            shadingStrength=F(shadingStrength,0,3,1);
            damage=F(damage,0,1,.45f);fray=F(fray,0,1,.55f);motifScale=F(motifScale,.05f,1,.75f);
            threadColor=C(threadColor,Color.white);surfaceColor=C(surfaceColor,Color.gray);accentColor=C(accentColor,Color.gray);
            colorAmount=F(colorAmount,0,1,0);
        }
        public static TexturePaintGarmentKind Kind(TexturePaintGarmentPreset p)
            => p>=TexturePaintGarmentPreset.FloralLace ? TexturePaintGarmentKind.Lace :
               p>=TexturePaintGarmentPreset.TigWeld ? TexturePaintGarmentKind.Welds :
               p>=TexturePaintGarmentPreset.LeatherWrap ? TexturePaintGarmentKind.Wraps :
               p<=TexturePaintGarmentPreset.Pleats ? TexturePaintGarmentKind.Wrinkles :
               p<=TexturePaintGarmentPreset.DirtyCuffs ? TexturePaintGarmentKind.Wear :
               p<=TexturePaintGarmentPreset.ReinforcementPanel ? TexturePaintGarmentKind.Pockets :
               p<=TexturePaintGarmentPreset.Eyelet ? TexturePaintGarmentKind.Hardware :
               p<=TexturePaintGarmentPreset.RepairPatch ? TexturePaintGarmentKind.Distress : TexturePaintGarmentKind.Labels;
        public static int Variant(TexturePaintGarmentPreset p)
        {
            int first=Kind(p) switch {TexturePaintGarmentKind.Wrinkles=>0,TexturePaintGarmentKind.Wear=>5,
                TexturePaintGarmentKind.Pockets=>10,TexturePaintGarmentKind.Hardware=>15,TexturePaintGarmentKind.Distress=>22,TexturePaintGarmentKind.Wraps=>32,
                TexturePaintGarmentKind.Welds=>37,TexturePaintGarmentKind.Lace=>41,_=>27};
            return (int)p-first;
        }
        public static string FamilyName(TexturePaintGarmentKind kind)=>kind switch
        {
            TexturePaintGarmentKind.Wrinkles=>"Wrinkles & Tension Folds",TexturePaintGarmentKind.Wear=>"Denim Wash & Garment Wear",
            TexturePaintGarmentKind.Pockets=>"Pockets & Garment Panels",TexturePaintGarmentKind.Hardware=>"Zippers, Closures & Hardware",
            TexturePaintGarmentKind.Distress=>"Distressing & Repairs",TexturePaintGarmentKind.Wraps=>"Wraps, Tape & Dressings",
            TexturePaintGarmentKind.Welds=>"Welds & Heat Discoloration",TexturePaintGarmentKind.Lace=>"Lace & Decorative Openwork",_=>"Labels, Patches & Prints"
        };
        public static TexturePaintGarmentSettings Create(TexturePaintGarmentPreset preset)
        {
            var s=new TexturePaintGarmentSettings{enabled=true,preset=preset,kind=Kind(preset),variant=Variant(preset)};
            s.metallic=s.kind==TexturePaintGarmentKind.Hardware;
            if(s.kind==TexturePaintGarmentKind.Welds)
            {
                s.detailSize=.003f;s.relief=.001f;s.colorAmount=1;s.metallic=true;s.falloff=.001f;
                s.surfaceColor=new Color(.57f,.6f,.63f,1);s.roughness=.27f;s.weldSpatter=.06f;
                if(preset==TexturePaintGarmentPreset.MigWeld){s.weldRipple=.8f;s.irregularity=.6f;s.weldSpatter=.5f;s.weldSoot=.32f;s.weldHeat=.25f;}
                if(preset==TexturePaintGarmentPreset.SolderSeam){s.weldRipple=.04f;s.weldHeat=0;s.weldSpatter=0;s.roughness=.16f;s.weldSoot=0;}
                if(preset==TexturePaintGarmentPreset.BrazedSeam){s.surfaceColor=new Color(.64f,.4f,.14f,1);s.weldRipple=.15f;s.weldHeat=.3f;s.weldSpatter=.02f;}
            }
            if(s.kind==TexturePaintGarmentKind.Lace)
            {
                s.detailSize=.035f;s.relief=.0005f;s.colorAmount=1;s.falloff=.001f;s.metallic=false;
                s.surfaceColor=new Color(.92f,.86f,.71f,1);s.roughness=.68f;s.irregularity=.08f;
                if(preset==TexturePaintGarmentPreset.GothicLace){s.surfaceColor=new Color(.06f,.045f,.055f,1);s.laceNet=.22f;s.laceScallop=.06f;}
                if(preset==TexturePaintGarmentPreset.ScallopedLace){s.laceScallop=.22f;s.laceThread=.015f;s.laceNet=.35f;}
            }
            if(s.kind==TexturePaintGarmentKind.Wrinkles){s.wear=.08f;s.recess=.12f;}
            if(s.kind==TexturePaintGarmentKind.Wear){s.relief=0;s.normal=false;s.height=false;s.roughnessOutput=false;s.wear=.55f;s.recess=.15f;}
            if(s.kind==TexturePaintGarmentKind.Pockets){s.detailSize=.015f;s.relief=.0012f;}
            if(s.kind==TexturePaintGarmentKind.Hardware){s.detailSize=.004f;s.relief=.0008f;s.colorAmount=1;}
            if(s.kind==TexturePaintGarmentKind.Distress){s.detailSize=.0025f;s.wear=.65f;}
            if(s.kind==TexturePaintGarmentKind.Labels){s.colorAmount=1;s.relief=.0006f;s.surfaceColor=new Color(.8f,.75f,.6f,1);s.accentColor=new Color(.15f,.1f,.06f,1);}
            if(s.kind==TexturePaintGarmentKind.Wraps)
            {
                s.detailSize=.014f;s.relief=.0006f;s.falloff=.001f;s.colorAmount=1;s.roughness=.8f;
                s.surfaceColor=new Color(.82f,.78f,.67f,1);s.accentColor=new Color(.3f,.12f,.065f,1);
                s.normal=false;s.height=true;s.metallic=false;
                if(preset==TexturePaintGarmentPreset.LeatherWrap){s.surfaceColor=new Color(.19f,.085f,.04f,1);s.roughness=.55f;s.wrapFray=.03f;}
                if(preset==TexturePaintGarmentPreset.GripTape){s.surfaceColor=new Color(.045f,.05f,.055f,1);s.roughness=.93f;s.wrapFray=0;s.wrapWeave=.00035f;}
                if(preset==TexturePaintGarmentPreset.GauzeWrap){s.wrapWeave=.0018f;s.wrapFray=.3f;}
                if(preset==TexturePaintGarmentPreset.AdhesiveDressing){s.surfaceColor=new Color(.68f,.43f,.28f,1);s.wrapAngle=0;s.wrapStain=0;s.wrapFray=0;s.roughness=.6f;}
            }
            switch(preset)
            {
                case TexturePaintGarmentPreset.CuffGather:s.detailSize=.007f;s.taper=.1f;break;
                case TexturePaintGarmentPreset.Pleats:s.irregularity=.06f;s.relief=.0025f;break;
                case TexturePaintGarmentPreset.KneeHoneycombs:s.detailSize=.018f;break;
                case TexturePaintGarmentPreset.DirtyCuffs:s.wear=.12f;s.dirt=.7f;break;
                case TexturePaintGarmentPreset.WeltPocket:s.opening=.07f;break;
                case TexturePaintGarmentPreset.Waistband:s.stitchRows=3;break;
                case TexturePaintGarmentPreset.MetalZipper:s.detailSize=.0018f;s.relief=.00065f;s.metalRoughness=.28f;break;
                case TexturePaintGarmentPreset.CoilZipper:s.detailSize=.0012f;s.relief=.0005f;s.metalRoughness=.42f;s.accentColor=new Color(.12f,.12f,.1f,1);break;
                case TexturePaintGarmentPreset.Buttonhole:s.relief=.0004f;s.roughness=.86f;break;
                case TexturePaintGarmentPreset.SewnButton:s.accentColor=new Color(.25f,.16f,.08f,1);s.repeat=3;break;
                case TexturePaintGarmentPreset.Eyelet:s.repeat=5;break;
                case TexturePaintGarmentPreset.Rivet:s.repeat=3;break;
                case TexturePaintGarmentPreset.DarnedRepair:s.detailSize=.0012f;s.damage=.25f;s.fray=.15f;break;
                case TexturePaintGarmentPreset.ExposedThreads:s.detailSize=.0009f;s.surfaceColor=new Color(.68f,.66f,.56f,1);s.relief=.0005f;break;
                case TexturePaintGarmentPreset.FrayedTear:s.detailSize=.0011f;s.surfaceColor=new Color(.73f,.70f,.59f,1);s.relief=.0007f;break;
                case TexturePaintGarmentPreset.WovenLabel:s.detailSize=.0004f;s.relief=.00035f;break;
                case TexturePaintGarmentPreset.LeatherPatch:s.surfaceColor=new Color(.4f,.2f,.085f,1);s.roughness=.55f;break;
                case TexturePaintGarmentPreset.PrintedLogo:s.relief=.0001f;s.colorAmount=0;s.stitchRows=0;break;
                case TexturePaintGarmentPreset.RubberBadge:s.surfaceColor=new Color(.07f,.07f,.07f,1);s.accentColor=Color.white;s.relief=.001f;break;
                case TexturePaintGarmentPreset.EmbroideredPatch:s.detailSize=.00065f;s.relief=.0008f;break;
            }
            return s;
        }
        /// <summary>Apply a material/construction look without replacing placement, output flags or links.</summary>
        public void ApplyZipperStyle(TexturePaintZipperStylePreset look)
        {
            zipperStyle=new TexturePaintZipperStyleSettings{preset=look,weave=TexturePaintZipperTapeWeave.Twill,
                weaveScale=.00075f,weaveContrast=.4f,weaveHeight=.08f,cordHeight=.65f};
            var z=zipperStyle;
            surfaceColor=new Color(.18f,.19f,.22f,1);accentColor=new Color(.72f,.75f,.8f,1);
            threadColor=new Color(.38f,.4f,.44f,1);z.cordColor=threadColor;
            roughness=.82f;metalRoughness=.22f;detailSize=.0018f;stitchSize=.00025f;stitchSpacing=.003f;stitchRows=2;edgeWidth=.035f;
            zipperWidth=1;relief=.00065f;
            zipperTeeth=new();zipperStops=new(){height=2.5f};zipperSlider=new(){height=1.5f};zipperPull=new(){height=2};zipperTang=new(){height=2};
            // Explicit tooth metallic keeps presets identical on metal and coil constructions.
            zipperTeeth.metallic=1;
            z.toothShape=TexturePaintZipperToothShape.Interlocking;
            switch(look)
            {
                case TexturePaintZipperStylePreset.AntiqueBrass:
                    accentColor=new Color(.66f,.45f,.16f,1);surfaceColor=new Color(.24f,.14f,.075f,1);
                    threadColor=new Color(.63f,.43f,.23f,1);z.cordColor=threadColor;
                    metalRoughness=.38f;z.weave=TexturePaintZipperTapeWeave.Herringbone;z.tapeWidth=.9f;break;
                case TexturePaintZipperStylePreset.GoldCoil:
                    z.toothShape=TexturePaintZipperToothShape.Coil;accentColor=new Color(.86f,.67f,.22f,1);
                    detailSize=.0012f;metalRoughness=.16f;z.tapeWidth=.62f;z.weave=TexturePaintZipperTapeWeave.Ribbed;
                    z.stitchStyle=TexturePaintZipperStitchStyle.Chain;z.stitchLength=.92f;stitchRows=1;break;
                case TexturePaintZipperStylePreset.BlackMolded:
                    z.toothShape=TexturePaintZipperToothShape.Molded;accentColor=new Color(.18f,.19f,.22f,1);
                    surfaceColor=new Color(.09f,.1f,.12f,1);
                    zipperTeeth.metallic=0;zipperTeeth.smoothness=.5f;detailSize=.0027f;
                    z.toothWidth=1.2f;z.tapeWidth=.88f;z.weave=TexturePaintZipperTapeWeave.Plain;
                    z.stitchStyle=TexturePaintZipperStitchStyle.Zigzag;stitchRows=1;break;
                case TexturePaintZipperStylePreset.BronzeRounded:
                    z.toothShape=TexturePaintZipperToothShape.Rounded;accentColor=new Color(.76f,.43f,.26f,1);
                    z.toothRoundness=1.5f;detailSize=.0023f;z.weave=TexturePaintZipperTapeWeave.Herringbone;
                    threadColor=new Color(.72f,.6f,.43f,1);break;
                case TexturePaintZipperStylePreset.SilverBars:
                    z.toothShape=TexturePaintZipperToothShape.Bars;detailSize=.0024f;z.toothLength=.65f;
                    z.tapeWidth=.8f;z.weave=TexturePaintZipperTapeWeave.Ribbed;metalRoughness=.12f;break;
                case TexturePaintZipperStylePreset.GoldChevron:
                    z.toothShape=TexturePaintZipperToothShape.Chevron;accentColor=new Color(.85f,.67f,.3f,1);
                    detailSize=.0025f;z.toothWidth=1.2f;z.bankOffset=0;z.tapeWidth=.85f;
                    threadColor=new Color(.65f,.54f,.29f,1);z.stitchStyle=TexturePaintZipperStitchStyle.Chain;z.stitchLength=.92f;break;
            }
            Normalize();
        }

        public IEnumerable<TexturePaintChannel> OutputChannels()
        {
            if(albedo)yield return TexturePaintChannel.Albedo;if(normal)yield return TexturePaintChannel.Normal;
            if(ao)yield return TexturePaintChannel.AmbientOcclusion;if(roughnessOutput)yield return TexturePaintChannel.Roughness;
            if(metallic)yield return TexturePaintChannel.Metallic;if(height)yield return TexturePaintChannel.NormalControl;
            if(masks)yield return TexturePaintChannel.Custom;
        }
        public bool SupportsAnyOutput(TextureSet set)
        {foreach(var c in OutputChannels())if(set?.GetChannel(c)!=null)return true;return false;}
        public void PopulateSources(StrokeContext context)
        {
            context.channelSources.Clear();foreach(var channel in OutputChannels())context.channelSources[channel]=
                new TexturePaintChannelSourceSettings{source=TexturePaintBrushSource.Color,color=Color.white};
        }
        public void Bind(MaterialPropertyBlock p,TexturePaintChannel channel,Vector2 size,
            TextureSet set=null,IReadOnlyList<TextureSet> sets=null,TexturePaintLayer owner=null, Inputs inputs=null)
        {
            var s=Clone();s.Normalize();p.SetInt("_GarmentEnabled",enabled?1:0);p.SetInt("_GarmentKind",(int)s.kind);
            p.SetInt("_GarmentVariant",s.variant);p.SetInt("_GarmentChannel",(int)channel);
            p.SetVector("_GarmentSize",new Vector4(Mathf.Max(.0001f,size.x),Mathf.Max(.0001f,size.y),s.detailSize,s.relief));
            p.SetVector("_GarmentShape",new Vector4(s.amount,s.angle*Mathf.Deg2Rad,s.irregularity,s.falloff));
            p.SetVector("_GarmentFold",new Vector4(s.focus.x,s.focus.y,s.spread,s.taper));
            p.SetVector("_GarmentAging",new Vector4(s.wear,s.recess,s.dirt,s.protection));
            p.SetVector("_GarmentStitch",new Vector4(s.stitchSize,s.stitchSpacing,s.stitchRows,s.edgeWidth));
            p.SetVector("_GarmentEnds",new Vector4(s.SupportsOpenEnds && s.openStart?1:0,s.SupportsOpenEnds && s.openEnd?1:0,0,0));
            p.SetVector("_GarmentFinish",new Vector4(s.roughness,s.metalRoughness,s.normalStrength,(uint)s.seed%65521));
            p.SetVector("_GarmentDetail",new Vector4(s.opening,s.slider,s.repeat,s.damage));
            p.SetVector("_GarmentWrap",new Vector4(s.wrapOverlap,s.wrapAngle*Mathf.Deg2Rad,s.wrapWrinkles,s.wrapLift));
            p.SetVector("_GarmentWrapSurface",new Vector4(s.wrapFray,s.wrapStain,s.wrapResidue,s.wrapWeave));
            p.SetVector("_GarmentWeld",new Vector4(s.weldWidth,s.weldRipple,s.weldHeat,s.weldHeatWidth));
            p.SetVector("_GarmentWeldFinish",new Vector4(s.weldSoot,s.weldSpatter,0,0));
            p.SetVector("_GarmentLace",new Vector4(s.laceThread,s.laceScallop,s.laceNet,s.laceTwist));
            p.SetFloat("_GarmentZipperWidth",s.zipperWidth);
            p.SetFloat("_GarmentZipperDepth",s.zipperDepth);
            var z=s.zipperStyle;
            p.SetInt("_GarmentToothShape",(int)z.toothShape);
            p.SetVector("_GarmentToothLayout",new Vector4(z.toothWidth,z.toothLength,z.toothRoundness,z.bankOffset));
            p.SetVector("_GarmentTape",new Vector4(z.tapeWidth*.5f,z.tapeHeight,z.weaveContrast,z.weaveHeight));
            p.SetVector("_GarmentWeave",new Vector4((int)z.weave,z.weaveScale>0?z.weaveScale:Mathf.Max(s.stitchSize*1.6f,.0002f),0,0));
            p.SetVector("_GarmentCord",new Vector4(z.cordWidth,z.cordHeight,z.cordInset,0));
            p.SetVector("_GarmentCordColor",z.cordColor.linear);
            p.SetVector("_GarmentZipperStitch",new Vector4((int)z.stitchStyle,z.stitchLength,z.stitchSpread,z.stitchHeight));
            p.SetFloat("_GarmentZipperThreadRoughness",z.stitchRoughness);
            p.SetVector("_GarmentZipperTeeth",s.zipperTeeth.ShaderValue(s.variant==0?1:0,s.metalRoughness));
            p.SetVector("_GarmentZipperStops",s.zipperStops.ShaderValue(1,s.metalRoughness));
            p.SetVector("_GarmentZipperSlider",s.zipperSlider.ShaderValue(1,s.metalRoughness));
            p.SetVector("_GarmentZipperPull",s.zipperPull.ShaderValue(1,s.metalRoughness));
            p.SetVector("_GarmentZipperTang",s.zipperTang.ShaderValue(1,s.metalRoughness));
            p.SetFloat("_GarmentDepth",s.depthStrength);
            p.SetFloat("_GarmentShading",s.shadingStrength);
            p.SetVector("_GarmentPrint",new Vector4(s.fray,s.motifScale,s.motifColor?1:0,s.colorAmount));
            // Explicit vectors avoid project-dependent SetColor conversion: working RGB is always linear.
            p.SetVector("_GarmentThread",s.threadColor.linear);p.SetVector("_GarmentCloth",s.surfaceColor.linear);p.SetVector("_GarmentAccent",s.accentColor.linear);
            Texture motif=TexturePaintSpriteSource.Resolve(s.motif,s.motifSprite,TexturePaintChannel.Albedo,TexturePaintNormalConvention.OpenGL);
            p.SetTexture("_GarmentMotif",motif!=null?motif:Texture2D.whiteTexture);p.SetInt("_GarmentHasMotif",motif!=null?1:0);
            BindInput(p,"Fold",s.foldInput,inputs?.fold,inputs?.foldOpacity??1);
            BindInput(p,"Protection",s.protectionInput,inputs?.protection,inputs?.protectionOpacity??1);
        }
        private static void BindInput(MaterialPropertyBlock p,string name,TexturePaintLayerReference reference,Texture image,float opacity)
        {
            p.SetTexture("_Garment"+name+"Input",image!=null?image:Texture2D.blackTexture);
            p.SetVector("_Garment"+name+"Read",new Vector4(image!=null?1:0,(int)(reference?.component??TexturePaintReferenceComponent.Red),reference?.invert==true?1:0,opacity));
        }
        /// <summary>Compose each input once per destination, then share it across generated channels.</summary>
        public sealed class Inputs:IDisposable
        {
            internal Texture fold,protection;
            internal float foldOpacity=1,protectionOpacity=1;
            private readonly List<RenderTexture> temporary=new List<RenderTexture>();
            public Inputs(TexturePaintGarmentSettings settings,TextureSet set,IReadOnlyList<TextureSet> sets,TexturePaintLayer owner)
            {
                try
                {
                    if(!settings.HasReferences)return;
                    fold=Read(settings.foldInput,set,sets,owner,out foldOpacity);
                    protection=Read(settings.protectionInput,set,sets,owner,out protectionOpacity);
                }
                catch { Dispose();throw; }
            }
            private Texture Read(TexturePaintLayerReference reference,TextureSet set,IReadOnlyList<TextureSet> sets,TexturePaintLayer owner,out float opacity)
            {
                opacity=1;if(reference?.IsSet!=true)return null;
                var source=TexturePaintLinkEvaluator.Resolve(sets??new[]{set},set,reference,out var sourceSet);
                if(source==null)throw new InvalidOperationException("A garment input source is missing. Cached output retained.");
                if(ReferenceEquals(owner,source))throw new InvalidOperationException("A garment input has a circular reference. Cached output retained.");
                bool mask=reference.component==TexturePaintReferenceComponent.Mask;
                if(!mask && sourceSet.compositor?.IsAvailable!=true)
                {
                    var options=source.GetChannelSettings(reference.channel,false);
                    opacity=source.visible && options?.enabled!=false ? source.opacity*(options?.opacity??1) : 0;
                }
                return TexturePaintLinkEvaluator.ReadSource(sourceSet,source,reference.channel,mask,temporary);
            }
            public void Dispose(){foreach(var texture in temporary)RenderTexture.ReleaseTemporary(texture);temporary.Clear();}
        }
    }
}
