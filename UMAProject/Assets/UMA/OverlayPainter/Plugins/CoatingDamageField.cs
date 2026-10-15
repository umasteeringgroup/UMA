using System.Collections.Generic;
using UnityEngine;
namespace UMA.TexturePaint.Examples
{
    /// <summary>Shared, continuous 3D coating construction used by Print Aging and corrosion.</summary>
    public sealed class CoatingDamageField
    {
        public readonly float amount;
        readonly int style,seed;readonly float scale,density,rimWidth,rimHeight,depth,rough,substrateRough,substrateMetal,rustMix,distortion,elongation,rupture;
        readonly Color coat,substrate;
        public struct SampleResult{public Color color;public float roughness,metallic,height,exposed,rim;}
        public static void AddParameters(List<TexturePaintPluginParameterDefinition> p,bool corrosion)
        {
            p.Add(MakeupFacePaintGeneratorPlugin.Header("coatingDamage","Coating Damage","Zero preserves the existing generator. Enabled coating replaces the covered finish with blisters, exposed chips or lifted flakes. Use a mask to restrict the coating."));
            p.Add(EyeIrisGeneratorPlugin.F("coatingAmount","Coating Damage Enable / Mix",0,1,0));
            p.Add(EyeIrisGeneratorPlugin.E("coatingStyle","Coating Construction",new[]{"Blistered paint","Chipped enamel","Peeling coated leather"},0));
            p.Add(EyeIrisGeneratorPlugin.F("coatingScale","Damage Cells / UV Unit or Meter",1,200,28));
            p.Add(EyeIrisGeneratorPlugin.F("coatingDistortion","Damage Boundary Distortion",0,1,.65f));
            p.Add(EyeIrisGeneratorPlugin.F("coatingElongation","Damage Elongation",1,3,1.6f));
            p.Add(EyeIrisGeneratorPlugin.F("coatingRupture","Ruptured Blister Fraction",0,1,.24f));
            p.Add(EyeIrisGeneratorPlugin.F("coatingDensity","Damaged Area",0,1,.55f));
            p.Add(EyeIrisGeneratorPlugin.C("coatingColor","Intact Coating",new Color(.18f,.28f,.32f,1)));
            p.Add(EyeIrisGeneratorPlugin.C("substrateColor","Exposed Substrate",new Color(.26f,.19f,.115f,1)));
            p.Add(EyeIrisGeneratorPlugin.F("coatingRoughness","Intact Coating Roughness",0,1,.3f));
            p.Add(EyeIrisGeneratorPlugin.F("substrateRoughness","Exposed Substrate Roughness",0,1,.72f));
            p.Add(EyeIrisGeneratorPlugin.F("substrateMetallic","Exposed Substrate Metallic",0,1,0));
            p.Add(EyeIrisGeneratorPlugin.F("coatingRimWidth","Damage Rim Width / Cell",.005f,.2f,.055f));
            p.Add(EyeIrisGeneratorPlugin.F("coatingRimHeight","Raised Rim Height",0,.2f,.025f));
            p.Add(EyeIrisGeneratorPlugin.F("coatingDepth","Substrate Recess",0,.2f,.018f));
            if(corrosion)p.Add(EyeIrisGeneratorPlugin.F("coatingRustMix","Corrosion on Exposed Substrate",0,1,.85f));
            else p.Add(EyeIrisGeneratorPlugin.B("wholeCoating","Treat Entire Layer as Coating",false));
        }
        public CoatingDamageField(TexturePaintPluginParameterSet p)
        {
            float F(string id,float v,float min,float max)=>SurfaceDepositionMaterial.F(p,id,v,min,max);
            distortion=F("coatingDistortion",.65f,0,1);elongation=F("coatingElongation",1.6f,1,3);rupture=F("coatingRupture",.24f,0,1);
            amount=F("coatingAmount",0,0,1);style=Mathf.Clamp(p.Integer("coatingStyle"),0,2);seed=p.Integer("seed",1731);
            scale=F("coatingScale",28,1,200);density=F("coatingDensity",.55f,0,1);rimWidth=F("coatingRimWidth",.055f,.005f,.2f);
            rimHeight=F("coatingRimHeight",.025f,0,.2f);depth=F("coatingDepth",.018f,0,.2f);
            rough=F("coatingRoughness",.3f,0,1);substrateRough=F("substrateRoughness",.72f,0,1);substrateMetal=F("substrateMetallic",0,0,1);rustMix=F("coatingRustMix",.85f,0,1);
            coat=p.LinearColor("coatingColor",new Color(.18f,.28f,.32f));substrate=p.LinearColor("substrateColor",new Color(.26f,.19f,.115f));
        }
        static float Noise(Vector3 p,int seed)=>(EyeIrisMaterial.Noise(new(p.x,p.y),seed)+EyeIrisMaterial.Noise(new(p.y+37,p.z),seed+71)+EyeIrisMaterial.Noise(new(p.z,p.x+19),seed+137))/3;
        static float S(float a,float b,float x)=>EyeIrisMaterial.Smooth(a,b,x);
        public SampleResult Sample(Vector3 world,Color original,float originalRough,float originalHeight,float originalMetal,bool corrosion,float aa=.002f)
        {
            if(amount<=0)return new SampleResult{color=original,roughness=originalRough,height=originalHeight,metallic=originalMetal};
            Vector3 p=world*scale;
            if(style!=0)
            {
                Vector3 bend=new(Noise(p*1.7f,seed+101)-.5f,Noise(p*1.7f+Vector3.one*13,seed+211)-.5f,Noise(p*1.7f+Vector3.one*29,seed+317)-.5f);
                p+=bend*(distortion*2.5f);
            }
            var cell=new Vector3Int(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y),Mathf.FloorToInt(p.z));
            float field=2;float winner=0;
            // Union signed distances, never choose a Voronoi owner then discard its neighbour.
            for(int z=-1;z<=1;z++)for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
            {
                var c=cell+new Vector3Int(x,y,z);int key=unchecked(c.x*73856093^c.y*19349663^c.z*83492791);
                float H(int stream)=>EyeIrisMaterial.Hash(key,stream,seed);
                if(H(1)>density)continue;
                Vector3 center=(Vector3)c+new Vector3(.2f+H(2)*.6f,.2f+H(3)*.6f,.2f+H(4)*.6f);
                Vector3 delta=p-center;
                float d;
                if(style==0)d=delta.magnitude-(.22f+H(5)*.32f);
                else
                {
                    float angle=H(7)*Mathf.PI*2,cs=Mathf.Cos(angle),sn=Mathf.Sin(angle);
                    Vector3 local=new(delta.x*cs+delta.y*sn,(-delta.x*sn+delta.y*cs)*Mathf.Lerp(1,elongation,H(8)),delta.z);
                    float angular=Mathf.Max(Mathf.Abs(local.x),Mathf.Max(Mathf.Abs(local.y),Mathf.Abs(local.z)));
                    float metric=Mathf.Lerp(local.magnitude,angular,style==1?.65f:.35f);
                    d=metric-(.2f+Mathf.Pow(H(5),.7f)*.62f);
                }
                if(d<field){field=d;winner=H(6);}
            }
            float grain=Noise(p*19,seed+91);float warped=field+(Noise(p*5,seed+17)-.5f)*(style==0?.07f:distortion*.4f);
            float filter=Mathf.Clamp(aa*scale,.003f,.15f);
            float hole=1-S(-filter,filter,warped);float ring=(1-S(rimWidth,rimWidth+filter,Mathf.Abs(warped)));
            float crown=Mathf.Sqrt(Mathf.Clamp01(-warped/.4f));
            float exposed=hole,raised=ring*rimHeight;
            if(style==0)
            {
                // Most blisters are closed domes. A minority rupture to reveal the substrate.
                float ruptured=winner>1-rupture?1:0;exposed=hole*ruptured;
                raised=crown*rimHeight*(1-ruptured)+ring*rimHeight*.6f*ruptured;
            }
            else if(style==2)
            {
                float curl=.25f+.75f*S(-.6f,.6f,Mathf.Sin(p.x*2+p.y*.7f+winner*6.28f));
                raised=ring*rimHeight*(1+curl*1.5f);
            }
            Color backing=corrosion?Color.Lerp(substrate,original,rustMix):substrate;
            float backingRough=corrosion?Mathf.Lerp(substrateRough,originalRough,rustMix):substrateRough;
            float backingMetal=corrosion?Mathf.Lerp(substrateMetal,originalMetal,rustMix):substrateMetal;
            Color paint=coat*(1+(grain-.5f)*.045f);Color target=Color.Lerp(paint,backing,exposed);
            target*=1-ring*(style==2?.18f:.065f);
            float targetHeight=raised-exposed*depth+(grain-.5f)*.00025f;
            return new SampleResult{color=Color.Lerp(original,target,amount),roughness=Mathf.Lerp(originalRough,Mathf.Lerp(rough,backingRough,exposed),amount),
                metallic=Mathf.Lerp(originalMetal,backingMetal*exposed,amount),height=Mathf.Lerp(originalHeight,targetHeight,amount),exposed=exposed,rim=ring};
        }
    }
}
