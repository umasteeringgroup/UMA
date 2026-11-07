using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    public enum BrowCurveMode { Constant, StartEnd, StartMiddleEnd, Curve, FromStart, FromEnd, Region }

    /// <summary>A reusable scalar modulation. Factors multiply values; angles add offsets.
    /// AnimationCurve is serialized by Unity, including weighted tangents and wrapping.</summary>
    [Serializable]
    public sealed class BrowParameter
    {
        public float value = 1;
        public BrowCurveMode mode;
        public bool additive;
        public float start = 1, middle = 1, end = 1, midpoint = .5f;
        public float regionStart = .25f, regionEnd = .75f, entrance = .1f, exit = .1f;
        public AnimationCurve curve = AnimationCurve.Linear(0,1,1,1);
        public AnimationCurve entranceCurve = AnimationCurve.Linear(0,0,1,1);
        public AnimationCurve exitCurve = AnimationCurve.Linear(0,1,1,0);
        public BrowParameter() { }
        public BrowParameter(float value, bool angle = false) { this.value=value; additive=angle; if(angle) start=middle=end=0; }
        public float Evaluate(float t)
        {
            t = Mathf.Clamp01(Finite(t,0));
            float factor = additive ? 0 : 1;
            switch(mode)
            {
                case BrowCurveMode.StartEnd: factor=Mathf.Lerp(start,end,t); break;
                case BrowCurveMode.StartMiddleEnd:
                    float m=Mathf.Clamp(Finite(midpoint,.5f),.001f,.999f);
                    factor=t<m?Mathf.Lerp(start,middle,t/m):Mathf.Lerp(middle,end,(t-m)/(1-m)); break;
                case BrowCurveMode.Curve: factor=curve?.Evaluate(t)??factor; break;
                case BrowCurveMode.FromStart: factor=Mathf.Lerp(end,start,1-Mathf.Clamp01(t/Mathf.Max(.001f,regionEnd))); break;
                case BrowCurveMode.FromEnd: factor=Mathf.Lerp(start,end,Mathf.Clamp01((t-regionStart)/Mathf.Max(.001f,1-regionStart))); break;
                case BrowCurveMode.Region:
                    float a=entranceCurve?.Evaluate(Mathf.Clamp01((t-regionStart)/Mathf.Max(.001f,entrance)))??1;
                    float b=exitCurve?.Evaluate(Mathf.Clamp01((t-regionEnd+exit)/Mathf.Max(.001f,exit)))??1;
                    factor=Mathf.Lerp(start,middle,Mathf.Clamp01(a*b)); break;
            }
            return Finite(additive ? value+factor : value*factor,0);
        }
        public static float Finite(float x,float fallback) => float.IsFinite(x)?x:fallback;
        public float[] Lookup(int count=257)
        { var values=new float[count]; for(int i=0;i<count;i++)values[i]=Evaluate(i/(float)(count-1)); return values; }
        public static BrowParameter Profile(float value,float a,float b,float c)
            => new BrowParameter(value){mode=BrowCurveMode.StartMiddleEnd,start=a,middle=b,end=c};
    }

    public enum BrowPreset { Natural, Straight, Arched, Sparse, Dense, Groomed, Bushy, Fine, Asymmetric }
    public enum BrowDiagnostic { Material, RootRegion, Roots, Growth, Density, StrandIds, Clumps, Coverage, Height, RootToTip }
    public enum BrowPopulationMode { TargetCount, DensityPerSquareWidth }
    public enum BrowSymmetryMode { ExactMirror, LinkedShapeIndependentHairs, Independent }
    [Serializable] public sealed class BrowSlit
    { public float position=.7f,width=.025f,angle=20,feather=.01f; public bool enabled=true; }

    [Serializable]
    public sealed class EyebrowSettings
    {
        public const int CurrentVersion=1;
        public int version=CurrentVersion, seed=173;
        public BrowPreset preset;
        public bool advanced, reverse, flipAcross;
        public BrowPopulationMode distribution;
        public int count=480;
        public BrowParameter areaDensity=new(100), density=BrowParameter.Profile(1,.78f,1,.28f);
        public BrowParameter densityAcross=BrowParameter.Profile(1,.35f,1,.18f);
        public BrowParameter fullness=new(.78f), upper=BrowParameter.Profile(1,.65f,1,.08f), lower=BrowParameter.Profile(1,.8f,1,.06f);
        public BrowParameter centerOffset=new(0), lengthScale=new(1), rotation=new(0,true), slant=new(0,true);
        public float archPosition=.58f;
        public BrowParameter archHeight=new(.05f), tailDrop=new(.08f), headTaper=new(.045f), tailTaper=new(.14f);
        public float distributionStart=0,distributionEnd=1;
        public BrowParameter edgeSoftness=new(.12f), thinning=new(0), containment=new(.35f), strayAllowance=new(.12f);
        public List<BrowSlit> slits=new();
        public BrowParameter length=BrowParameter.Profile(.48f,.65f,1,.72f), thickness=new(.009f), opacity=new(.92f), bend=new(.22f);
        public BrowParameter thicknessAcross=BrowParameter.Profile(1,.9f,1,.62f), lengthAcross=BrowParameter.Profile(1,.85f,1,.58f);
        public BrowParameter strandWidth=BrowParameter.Profile(1,.45f,1,0), strandOpacity=new(1);
        public BrowParameter rootFade=new(.065f), tipFade=new(.12f), rootTaper=new(.07f), tipTaper=new(.65f), sharpness=new(1.3f);
        public bool trimmedEnds;
        public AnimationCurve rootFadeCurve=AnimationCurve.EaseInOut(0,0,1,1), tipFadeCurve=AnimationCurve.Linear(0,0,1,1);
        public AnimationCurve rootTaperCurve=AnimationCurve.Linear(0,.3f,1,1), tipTaperCurve=AnimationCurve.Linear(0,0,1,1);
        public BrowParameter headAngle=new(77,true), bodyAngle=new(25,true), archAngle=new(9,true), tailAngle=new(-9,true);
        public float headEnd=.23f,tailStart=.72f;
        public BrowParameter hairSlant=new(0,true), upperAngle=new(-15,true), lowerAngle=new(16,true), convergence=new(.54f), convergenceStrength=new(.45f);
        public BrowParameter rootJitter=new(.55f), densityVariation=new(.12f), lengthVariation=new(.22f), thicknessVariation=new(.18f);
        public BrowParameter directionVariation=new(9), bendVariation=new(.3f), waviness=new(.012f), waveVariation=new(.3f);
        public BrowParameter clumpVariation=new(.2f), colorVariation=new(.14f), opacityVariation=new(.12f), flyaways=new(.025f);
        public BrowParameter broadVariation=new(.16f), clumpStrength=new(.18f), clumpSpacing=new(.15f), clumpSize=new(.2f);
        public BrowParameter strandClump=BrowParameter.Profile(1,0,.35f,1), tipSpread=new(.07f), guideVariation=new(8);
        public bool preserveLength=true;
        public BrowParameter secondary=new(.3f), secondaryLength=new(.65f), secondaryThickness=new(.65f);
        public BrowParameter baby=new(.12f), babyLength=new(.38f), babyThickness=new(.4f);
        public Color color=new(.075f,.041f,.025f,1), tipColor=new(.13f,.075f,.04f,1), grayColor=new(.52f,.48f,.43f,1);
        public BrowParameter colorAlongStrand=BrowParameter.Profile(1,0,.15f,.55f), grayFraction=new(.02f);
        public BrowParameter layerDepthContrast=new(.18f);
        public BrowParameter roughness=new(.58f), roughnessVariation=new(.07f), relief=new(.065f), ao=new(.035f);
        public BrowSymmetryMode symmetry;
        public int sideSeed=7919;
        public BrowParameter asymmetry=new(.12f);
        public bool finalQuality=true;
        public int maximumRaster=2048;
        public BrowDiagnostic diagnostic;

        public static EyebrowSettings Load(string json)
        {
            var s=new EyebrowSettings();
            if(!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json,s);
            if(s.version>CurrentVersion) throw new InvalidOperationException("This eyebrow was saved by a newer generator. Its settings and last output have been preserved.");
            // Version zero used the same field names. New fields retain initialized defaults.
            s.version=CurrentVersion; s.count=Mathf.Clamp(s.count,0,6000);
            s.maximumRaster=Mathf.Clamp(s.maximumRaster,128,8192);
            var defaults=new EyebrowSettings();
            foreach(var field in typeof(EyebrowSettings).GetFields())
            {
                if(field.FieldType==typeof(BrowParameter))
                {
                    var p=(BrowParameter)field.GetValue(s);
                    if(p==null){field.SetValue(s,field.GetValue(defaults));continue;}
                    p.value=BrowParameter.Finite(p.value,((BrowParameter)field.GetValue(defaults)).value);
                    p.curve??=AnimationCurve.Linear(0,p.additive?0:1,1,p.additive?0:1);
                    p.entranceCurve??=AnimationCurve.Linear(0,0,1,1);p.exitCurve??=AnimationCurve.Linear(0,1,1,0);
                }
                else if(field.FieldType==typeof(float))field.SetValue(s,BrowParameter.Finite((float)field.GetValue(s),(float)field.GetValue(defaults)));
                else if(field.FieldType==typeof(AnimationCurve)&&field.GetValue(s)==null)field.SetValue(s,field.GetValue(defaults));
            }
            s.archPosition=Mathf.Clamp(s.archPosition,.05f,.95f);s.headEnd=Mathf.Clamp(s.headEnd,.01f,s.archPosition);
            s.tailStart=Mathf.Clamp(s.tailStart,s.archPosition,.99f);
            s.slits??=new List<BrowSlit>();
            return s;
        }
        public string Save() => JsonUtility.ToJson(this);
        public EyebrowSettings Clone() => Load(Save());
        public static EyebrowSettings Preset(BrowPreset preset)
        {
            var s=new EyebrowSettings {preset=preset};
            switch(preset)
            {
                case BrowPreset.Straight: s.archHeight.value=0;s.tailDrop.value=0;s.tailAngle.value=4;break;
                case BrowPreset.Arched: s.archHeight.value=.3f;s.tailDrop.value=.22f;s.upper.midpoint=.65f;break;
                case BrowPreset.Sparse: s.count=230;s.thickness.value=.007f;s.thinning=BrowParameter.Profile(.5f,.2f,.8f,.4f);break;
                case BrowPreset.Dense: s.count=780;s.fullness.value=.9f;s.secondary.value=.4f;break;
                case BrowPreset.Groomed: s.containment.value=.85f;s.directionVariation.value=3;s.flyaways.value=.003f;s.clumpStrength.value=.35f;break;
                case BrowPreset.Bushy: s.count=700;s.length.value=.7f;s.directionVariation.value=16;s.flyaways.value=.075f;s.fullness.value=1;s.containment.value=.1f;break;
                case BrowPreset.Fine: s.count=380;s.thickness.value=.006f;s.color=new Color(.22f,.12f,.065f,1);s.relief.value=.035f;break;
                case BrowPreset.Asymmetric: s.symmetry=BrowSymmetryMode.LinkedShapeIndependentHairs;s.asymmetry.value=.22f;break;
            }
            return s;
        }
    }
}
