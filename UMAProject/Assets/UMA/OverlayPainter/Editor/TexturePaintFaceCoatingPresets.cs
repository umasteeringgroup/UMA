using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
namespace UMA.TexturePaint.Editor
{
    public static class TexturePaintFaceCoatingPresets
    {
        public const string Folder="Assets/UMA/OverlayPainter/Presets/Face and Coatings";
        public static readonly string[] Names={"Natural Rose","Evening Berry","Bronze Editorial","Gothic Velvet","War Paint","Festival Color",
            "Blistered Naval Paint","Chipped Ivory Enamel","Peeling Oxblood Leather","Weathered Teal Enamel","Aged Black Coated Canvas","Salt-worn Brown Leather"};
        [MenuItem("UMA/Overlay Painter/Create Missing Face and Coating Presets")]
        public static async void CreateMenu(){try{await CreateMissing();}catch(Exception e){Debug.LogException(e);}}
        public static async Task<int> CreateMissing()
        {
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();int count=0;
            using var host=new PluginHost();host.Discover();
            for(int index=0;index<Names.Length;index++)
            {
                string path=Folder+"/"+Names[index]+".asset";if(File.Exists(path))continue;
                var preset=Build(index,host);
                if(index>=6)
                {
                    var thumbnail=await TexturePaintClothingPresets.RenderThumbnail(preset,host,192);
                    string image=Folder+"/"+Names[index]+".png";try{File.WriteAllBytes(image,thumbnail.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(thumbnail);}
                    AssetDatabase.ImportAsset(image);preset.thumbnail=AssetDatabase.LoadAssetAtPath<Texture2D>(image);
                }
                AssetDatabase.CreateAsset(preset,path);count++;
            }
            AssetDatabase.SaveAssets();return count;
        }
        public static TexturePaintMaterialPreset Build(int index,PluginHost host)
        {
            var preset=ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();preset.displayName=Names[index];preset.category=index<6?"Face / Finished Looks":"Coatings / Finished Materials";
            preset.author="UMA";preset.includesCachedPluginOutput=false;preset.includesWholeStack=false;
            preset.portability=TexturePaintPresetPortability.RequiresPlugin|(index<6?TexturePaintPresetPortability.MeshDependent:TexturePaintPresetPortability.Portable);
            preset.description=index<6?"Editable cosmetic layers located from posed facial landmarks. Refine boundaries for the face with each layer's controls or mask. No base skin is included.":"Editable foundation, coating damage and finish layers. Damage rims and recessed substrate use Normal Control. Masks keep finishing passes restrained.";
            if(index<6)
            {
                if(index<4)
                {
                    var blush=Makeup("Blush",3,new Color(.65f,.22f,.24f),index==0?.2f:.28f,0);Set(blush,"feather",.9f);Set(blush,"gradient",.15f);
                    var shadow=Makeup("Eyeshadow",2,index==2?new Color(.44f,.27f,.1f):index==3?new Color(.12f,.07f,.15f):new Color(.32f,.15f,.2f),index==0?.3f:.7f,1);Set(shadow,"feather",.8f);
                    var liner=Makeup("Eyeliner",1,new Color(.035f,.023f,.03f),index==0?.62f:.95f,0);Set(liner,"offsetY",-.05f);Set(liner,"crease",.09f);Set(liner,"wing",index==0?.04f:.12f);Set(liner,"linerWidth",index==0?.014f:.025f);
                    Makeup("Lipstick",0,index==0?new Color(.53f,.24f,.26f):index==1?new Color(.34f,.025f,.095f):index==2?new Color(.47f,.21f,.12f):new Color(.10f,.016f,.05f),index==0?.5f:.9f,index==1?2:index==3?0:1);
                }
                else
                {
                    var paint=Makeup(index==4?"Charcoal War Paint":"Festival Stripes",index==4?4:6,index==4?new Color(.055f,.07f,.06f):new Color(.035f,.5f,.57f),.92f,0);
                    Set(paint,"edgeColor",index==4?new Color(.025f,.03f,.02f):new Color(.85f,.17f,.38f));Set(paint,"gradient",.8f);
                    var shadow=Makeup("Eye Accents",index==4?1:5,index==4?new Color(.07f,.08f,.065f):new Color(.65f,.19f,.48f),.7f,1);
                    Set(shadow,"wing",.16f);if(index==5)Makeup("Gloss Lip Tint",0,new Color(.64f,.12f,.22f),.55f,2);
                }
            }
            else
            {
                bool leather=index==8||index==11,canvas=index==10;
                var foundation=Add(preset,host,leather?"com.uma.texturepaint.leather-coated-fabric":"com.uma.texturepaint.cloth-texture",leather?"Leather foundation":"Surface foundation");
                Set(foundation,"amount",1);Set(foundation,"color",new Color(.18f,.09f,.045f));Set(foundation,"baseColor",new Color(.11f,.13f,.14f));Set(foundation,"threadColor",new Color(.14f,.16f,.17f));Set(foundation,"weaveScale",canvas?100:180);Set(foundation,"heightStrength",canvas?.007f:.0004f);Set(foundation,"depthStrength",canvas?.003f:.0004f);Set(foundation,"relief",.006f);Set(foundation,"crease",.12f);
                bool rust=index==6||index==9;var coating=Add(preset,host,rust?"com.uma.texturepaint.rust-corrosion":"com.uma.texturepaint.print-aging","Coating — editable damage");
                Set(coating,"projection",0);Set(coating,"coatingAmount",1);Set(coating,"wholeCoating",true);Set(coating,"coatingStyle",index==6?0:leather||canvas?2:1);
                Color[] coats={new(.1f,.19f,.28f),new(.83f,.79f,.67f),new(.23f,.035f,.055f),new(.08f,.35f,.32f),new(.035f,.04f,.046f),new(.3f,.14f,.06f)};
                Set(coating,"coatingColor",coats[index-6]);Set(coating,"coatingDensity",index==9?.7f:leather?.45f:.5f);Set(coating,"coatingScale",index==6?20:leather?14:18);
                Set(coating,"coatingRimHeight",leather?.008f:.009f);Set(coating,"coatingDepth",.007f);Set(coating,"coatingRoughness",leather?.48f:canvas?.4f:.24f);
                Set(coating,"substrateColor",leather?new Color(.32f,.19f,.1f):new Color(.32f,.34f,.37f));Set(coating,"substrateMetallic",leather||canvas?0:1);Set(coating,"substrateRoughness",leather?.85f:.45f);Set(coating,"outputMetallic",true);
                Set(coating,"height",.025f);Set(coating,"depth",.02f);
                var finish=Add(preset,host,"com.uma.texturepaint.surface-residue",index==11?"Salt deposition":"Subtle handling / residue");
                Set(finish,"style",index==11?3:index==6||index==9?2:0);Set(finish,"amount",index==11?.3f:.18f);Set(finish,"size",.08f);Set(finish,"count",4);Set(finish,"roughness",index==11?.85f:.26f);
                var layer=preset.layers.Last();layer.hasMask=true;layer.maskBaseValue=1;
                layer.maskEffects.stack.Add(new TexturePaintMaskEffect{kind=TexturePaintMaskEffectKind.Noise,name="Uneven surface contact",blend=TexturePaintMaskBlend.Multiply,opacity=.35f,tiling=new Vector2(7,7),seed=812});
            }
            foreach(var layer in preset.layers)
            {
                var d=host.FindCommand(layer.pluginId).Descriptor;
                foreach(TexturePaintChannel ch in Enum.GetValues(typeof(TexturePaintChannel)))
                    if(d.Declares(ch)&&layer.pluginParameters.Boolean("output"+ch,true)&&!layer.channels.Any(c=>c.channel==ch))
                    {
                        var saved=new TexturePaintDocumentLayerChannel{channel=ch};saved.settings.channel=ch;saved.settings.blendMode=ch==TexturePaintChannel.NormalControl?TexturePaintBlendMode.Overlay:TexturePaintBlendMode.Normal;layer.channels.Add(saved);
                        if(!preset.channels.Any(c=>c.channel==ch))preset.channels.Add(new TexturePaintMaterialPresetChannel{channel=ch,required=true});
                    }
                layer.pluginParametersJson=JsonUtility.ToJson(layer.pluginParameters);
            }
            return preset;
            TexturePaintPluginParameterSet Makeup(string name,int style,Color color,float opacity,int finish)
            {
                var p=Add(preset,host,"com.uma.texturepaint.makeup-face-paint",name);Set(p,"style",style);Set(p,"color",color);Set(p,"edgeColor",color*.72f);Set(p,"opacity",opacity);Set(p,"finish",finish);return p;
            }
        }
        static TexturePaintPluginParameterSet Add(TexturePaintMaterialPreset preset,PluginHost host,string id,string name)
        {
            var plugin=host.FindCommand(id)??throw new InvalidOperationException("Missing generator "+id);var d=plugin.Descriptor;var p=host.CreateParameters(plugin);
            var layer=new TexturePaintDocumentLayer{name=name,kind=TexturePaintLayerKind.Plugin,pluginId=id,pluginVersion=d.pluginVersion,pluginParameters=p};
            foreach(TexturePaintChannel ch in Enum.GetValues(typeof(TexturePaintChannel)))if(d.Declares(ch)&&p.Boolean("output"+ch,true))
            {
                var saved=new TexturePaintDocumentLayerChannel{channel=ch};saved.settings.channel=ch;saved.settings.blendMode=ch==TexturePaintChannel.NormalControl?TexturePaintBlendMode.Overlay:TexturePaintBlendMode.Normal;layer.channels.Add(saved);
                if(!preset.channels.Any(c=>c.channel==ch))preset.channels.Add(new TexturePaintMaterialPresetChannel{channel=ch,required=true});
            }
            preset.layers.Add(layer);if(!preset.plugins.Any(x=>x.pluginId==id))preset.plugins.Add(new TexturePaintMaterialPresetPlugin{pluginId=id,pluginVersion=d.pluginVersion,apiVersion=d.apiVersion,declaredChannels=d.declaredChannels,readChannels=d.readChannels,requiredMeshMaps=d.requiredMeshMaps,targets=d.supportedTargets});return p;
        }
        static void Set(TexturePaintPluginParameterSet p,string id,float v)=>p.Get(id,true).number=v;
        static void Set(TexturePaintPluginParameterSet p,string id,bool v)=>p.Get(id,true).boolean=v;
        static void Set(TexturePaintPluginParameterSet p,string id,Color v)=>p.Get(id,true).color=v;
    }
}
