using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public static class TexturePaintClothingPresets
    {
        public const string Folder="Assets/UMA/OverlayPainter/Presets/Clothing Finishes";

        [MenuItem("UMA/Overlay Painter/Create Missing Clothing Finish Presets")]
        public static async void CreateFromMenu()
        {try{await CreateMissing();}catch(Exception exception){Debug.LogException(exception);}}

        public static async Task<int> CreateMissing()
        {
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            using var host=new PluginHost();host.Discover();int count=0;
            string[] names={"Rain-soaked Navy Cotton","Sweat-darkened Jersey","Dried Salt on Charcoal Cotton","Chestnut Soft Leather","Weathered Brown Leather","Sand Suede","Black Coated Fabric"};
            Color[] colors={new(.11f,.18f,.29f),new(.35f,.4f,.44f),new(.16f,.17f,.18f),new(.30f,.14f,.065f),new(.23f,.115f,.05f),new(.56f,.43f,.29f),new(.065f,.065f,.075f)};
            for(int i=0;i<names.Length;i++)
            {
                string path=Folder+"/"+names[i]+".asset";if(File.Exists(path))continue;
                var preset=ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();
                preset.displayName=names[i];preset.category="Clothing Finishes";preset.author="UMA";
                preset.description=i<3?"A fabric foundation and independently editable moisture/residue layer. Mask the finish layer to place wet areas.":"Editable leather or coating with coordinated color, roughness and grain relief.";
                preset.includesCachedPluginOutput=false;preset.includesWholeStack=false;
                preset.portability=TexturePaintPresetPortability.RequiresPlugin;
                preset.tags.AddRange(new[]{"clothing",i<3?"wetness":"leather","procedural"});
                if(i<3)
                {
                    var fabric=Add(preset,host,"com.uma.texturepaint.cloth-texture","Fabric Foundation");
                    Set(fabric,"baseColor",colors[i]);Set(fabric,"threadColor",colors[i]*1.12f);
                    Set(fabric,"weave",i==1?1:0);Set(fabric,"weaveScale",96);Set(fabric,"roughness",.82f);Set(fabric,"heightStrength",.04f);
                    var finish=Add(preset,host,"com.uma.texturepaint.wetness-sweat","Moisture / Residue");
                    Set(finish,"profile",i);Set(finish,"amount",i==0?.78f:.58f);Set(finish,"scale",i==0?5:8);
                    Set(finish,"drying",i==2?.92f:i==1?.38f:.12f);Set(finish,"relief",.025f);
                }
                else
                {
                    var finish=Add(preset,host,"com.uma.texturepaint.leather-coated-fabric",names[i]);
                    Set(finish,"profile",i-3);Set(finish,"color",colors[i]);Set(finish,"amount",1);
                    Set(finish,"grain",i==5?150:90);Set(finish,"roughness",i==6?.3f:i==5?.88f:.56f);
                    Set(finish,"wear",i==4?.45f:.12f);Set(finish,"crease",i==4?.5f:.22f);Set(finish,"relief",i==5?.035f:.065f);
                }
                foreach(var layer in preset.layers)layer.pluginParametersJson=JsonUtility.ToJson(layer.pluginParameters);
                Texture2D thumbnail=await RenderThumbnail(preset,host,192);
                string imagePath=Folder+"/"+names[i]+".png";
                try{File.WriteAllBytes(imagePath,thumbnail.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(thumbnail);}
                AssetDatabase.ImportAsset(imagePath,ImportAssetOptions.ForceSynchronousImport);preset.thumbnail=AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
                AssetDatabase.CreateAsset(preset,path);count++;
            }
            AssetDatabase.SaveAssets();return count;
        }

        private static TexturePaintPluginParameterSet Add(TexturePaintMaterialPreset preset,PluginHost host,string id,string name)
        {
            var plugin=host.FindCommand(id)??throw new InvalidOperationException("Missing generator: "+id);
            var descriptor=plugin.Descriptor;var parameters=host.CreateParameters(plugin);
            var layer=new TexturePaintDocumentLayer{name=name,kind=TexturePaintLayerKind.Plugin,pluginId=id,pluginVersion=descriptor.pluginVersion,pluginParameters=parameters};
            foreach(TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
            {
                if(!descriptor.Declares(channel)||!parameters.Boolean("output"+channel,true))continue;
                var saved=new TexturePaintDocumentLayerChannel{channel=channel};saved.settings.channel=channel;
                saved.settings.blendMode=channel==TexturePaintChannel.NormalControl?TexturePaintBlendMode.Overlay:TexturePaintBlendMode.Normal;
                layer.channels.Add(saved);
                if(!preset.channels.Any(c=>c.channel==channel))preset.channels.Add(new TexturePaintMaterialPresetChannel{channel=channel,required=true});
            }
            preset.layers.Add(layer);
            preset.plugins.Add(new TexturePaintMaterialPresetPlugin{pluginId=id,pluginVersion=descriptor.pluginVersion,apiVersion=descriptor.apiVersion,
                declaredChannels=descriptor.declaredChannels,readChannels=descriptor.readChannels,requiredMeshMaps=descriptor.requiredMeshMaps,targets=descriptor.supportedTargets});
            return parameters;
        }
        private static void Set(TexturePaintPluginParameterSet p,string id,float value)=>p.Get(id,true).number=value;
        private static void Set(TexturePaintPluginParameterSet p,string id,Color value)=>p.Get(id,true).color=value;

        internal static async Task<Texture2D> RenderThumbnail(TexturePaintMaterialPreset preset,PluginHost host,int size)
        {
            var composite=new Dictionary<TexturePaintChannel,Color[]>();
            foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.Normal,TexturePaintChannel.NormalControl,TexturePaintChannel.Metallic})
                composite[channel]=Enumerable.Repeat(channel==TexturePaintChannel.Normal?new Color(.5f,.5f,1,1):channel==TexturePaintChannel.NormalControl?new Color(.5f,.5f,.5f,1):new Color(.18f,.18f,.18f,1),size*size).ToArray();
            foreach(var layer in preset.layers)
            {
                var plugin=host.FindCommand(layer.pluginId);var images=new Dictionary<string,TexturePaintReadOnlyImage>();var info=new Dictionary<string,TexturePaintReadOnlyChannelInfo>();
                foreach(var pair in composite)
                {
                    string key=TexturePaintReadContextV2.Key("preview",pair.Key);
                    images[key]=new TexturePaintReadOnlyImage("preview",pair.Key,size,size,false,pair.Value);
                    info[key]=new TexturePaintReadOnlyChannelInfo("preview",pair.Key,size,size,false);
                }
                var source=new TexturePaintReadContextV2(images,info,null,null,new List<string>{"preview"});
                var context=new TexturePaintCommandContextV2(plugin.Descriptor,source,layer.pluginParameters,CancellationToken.None,null,64L*1024*1024);
                await plugin.ExecuteAsync(context);
                foreach(var command in context.SealAndSnapshot())
                {
                    command.MaterializeCompactPixels();var target=composite[command.channel];
                    for(int y=0;y<command.rect.height;y++)for(int x=0;x<command.rect.width;x++)
                    {
                        int index=(command.rect.y+y)*size+command.rect.x+x;Color pixel=command.GetPixel(y*command.rect.width+x);
                        if(command.channel==TexturePaintChannel.NormalControl)
                        {float value=target[index].r+(pixel.r-.5f)*pixel.a;target[index]=new Color(value,value,value,1);}
                        else target[index]=Color.Lerp(target[index],pixel,pixel.a);
                    }
                    command.ReleaseMaterializedCompactPixels();
                }
            }
            var output=new Color[size*size];var height=composite[TexturePaintChannel.NormalControl];
            Vector3 light=new Vector3(-.4f,.5f,1).normalized;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                int i=y*size+x;
                float dx=height[y*size+Math.Min(size-1,x+1)].r-height[y*size+Math.Max(0,x-1)].r;
                float dy=height[Math.Min(size-1,y+1)*size+x].r-height[Math.Max(0,y-1)*size+x].r;
                Vector3 normal=new Vector3(-dx*35,-dy*35,1).normalized;
                float r=composite[TexturePaintChannel.Roughness][i].r;
                float diffuse=.32f+.68f*Mathf.Max(0,Vector3.Dot(normal,light));
                float specular=Mathf.Pow(Mathf.Max(0,Vector3.Dot(normal,(light+Vector3.forward).normalized)),Mathf.Lerp(90,5,r))*.08f*(1-r);
                Color color=composite[TexturePaintChannel.Albedo][i]*diffuse+Color.white*specular;color.a=1;output[i]=color.gamma;
            }
            var result=new Texture2D(size,size,TextureFormat.RGBA32,false);result.SetPixels(output);result.Apply();return result;
        }
    }
}
