using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UMA.TexturePaint.Editor;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Examples.Editor
{
    /// <summary>Original, editable examples. Companion package only; no main-plugin dependency on examples.</summary>
    public static class ArtistGeneratorExamples
    {
        public const string Folder="Assets/UMA/OverlayPainterExamples/Artist Generators";
        [MenuItem("UMA/Overlay Painter/Examples/Create Eye, Wrap and Embroidery Presets")]
        public static void CreatePresets()
        {
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            using var host=new PluginHost();host.Discover();
            for(int look=0;look<5;look++)
            {
                string name=new[]{"Blue Iris","Brown Iris","Green Iris","Hazel Iris","Gray Iris"}[look];
                Create(name,"com.uma.texturepaint.eye-iris",p=>{p.Get("look").number=look;});
            }
            var motif=Image("Crest Motif",false);var regions=Image("Crest Regions",true);
            for(int style=1;style<=2;style++)
            {
                int selected=style;
                Create(style==1?"Gold Satin Crest":"Colored Tatami Crest","com.uma.texturepaint.textile-surface",p=>
                {
                    p.Get("mode").number=1;p.Get("embroideryStyle").number=selected;p.Get("patternTiling").number=1;
                    p.Get("pattern").texture=motif;p.Get("threadDensity").number=115;p.Get("borderWidth").number=.025f;
                    p.Get("accentColor").color=new Color(.75f,.56f,.2f);p.Get("outputAO").boolean=true;
                    if(selected==2)p.Get("threadRegions").texture=regions;
                });
            }
            foreach(var kind in new[]{TexturePaintGarmentPreset.LeatherWrap,TexturePaintGarmentPreset.ClothBandage,
                TexturePaintGarmentPreset.GripTape,TexturePaintGarmentPreset.GauzeWrap,TexturePaintGarmentPreset.AdhesiveDressing})
            {
                string path=Folder+"/"+kind+".asset";if(File.Exists(path))continue;
                var asset=ScriptableObject.CreateInstance<TexturePaintGarmentPresetAsset>();asset.settings=TexturePaintGarmentSettings.Create(kind);
                AssetDatabase.CreateAsset(asset,path);
            }
            AssetDatabase.SaveAssets();
            void Create(string name,string id,Action<TexturePaintPluginParameterSet> configure)
            {
                string path=Folder+"/"+name+".asset";if(File.Exists(path))return;
                var plugin=host.FindCommand(id)??throw new InvalidOperationException("Generator unavailable: "+id);
                var d=plugin.Descriptor;var p=host.CreateParameters(plugin);configure(p);
                var asset=ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();
                asset.displayName=name;asset.category="Artist Generators";asset.author="UMA";
                asset.description=id.Contains("eye-iris")?"UMA 3 shared eye UV material; apply to the eye slot, not the cornea.":"Transparent embroidered crest; apply above a fabric layer. All controls remain editable.";
                asset.includesCachedPluginOutput=false;asset.includesWholeStack=false;
                asset.portability=TexturePaintPresetPortability.RequiresPlugin|TexturePaintPresetPortability.UVDependent;
                var layer=new TexturePaintDocumentLayer{name=name,kind=TexturePaintLayerKind.Plugin,pluginId=id,pluginVersion=d.pluginVersion,pluginParameters=p,pluginParametersJson=JsonUtility.ToJson(p)};
                foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl})
                {
                    var saved=new TexturePaintDocumentLayerChannel{channel=channel};saved.settings.channel=channel;
                    saved.settings.blendMode=channel==TexturePaintChannel.NormalControl?TexturePaintBlendMode.Overlay:TexturePaintBlendMode.Normal;
                    layer.channels.Add(saved);asset.channels.Add(new TexturePaintMaterialPresetChannel{channel=channel,required=true});
                }
                asset.layers.Add(layer);asset.plugins.Add(new TexturePaintMaterialPresetPlugin{pluginId=id,pluginVersion=d.pluginVersion,
                    apiVersion=d.apiVersion,declaredChannels=d.declaredChannels,readChannels=d.readChannels,targets=d.supportedTargets});
                AssetDatabase.CreateAsset(asset,path);
            }
        }
        private static Texture2D Image(string name,bool regions)
        {
            string path=Folder+"/"+name+".png";
            if(!File.Exists(path))
            {
                const int n=256;var texture=new Texture2D(n,n,TextureFormat.RGBA32,false,true);var pixels=new Color[n*n];
                for(int y=0;y<n;y++)for(int x=0;x<n;x++)
                {
                    Vector2 p=new Vector2((x+.5f)/n,(y+.5f)/n)-Vector2.one*.5f;
                    float angle=Mathf.Atan2(p.y,p.x),radius=.29f+.09f*Mathf.Cos(angle*5);
                    float mask=Mathf.SmoothStep(0,1,Mathf.Clamp01((radius-p.magnitude)*n));
                    if(p.magnitude<.075f)mask=0;
                    pixels[y*n+x]=regions?(p.x<-.07f?Color.red:p.x>.07f?Color.blue:Color.green):new Color(mask,mask,mask,1);
                }
                texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [MenuItem("UMA/Overlay Painter/Examples/Create UMA Eye Projects")]
        public static async void CreateEyeProjectsFromMenu()
        {try{await CreateEyeProjects();}catch(Exception e){Debug.LogException(e);}}
        public static async Task<string> CreateEyeProjects()
        {
            CreatePresets();
            const string slotPath="Assets/UMA/UMA3/Races/Slots/UMA30_Eyes/UMA30_Eyes_slot.asset";
            var slot=AssetDatabase.LoadAssetAtPath<SlotDataAsset>(slotPath);
            var overlay=AssetDatabase.LoadAssetAtPath<OverlayDataAsset>("Assets/UMA/UMA3/Races/Slots/UMA30_Eyes/UMA30_Eyes_Fem_Eyes_overlay.asset");
            if(slot==null||overlay==null)throw new InvalidOperationException("Install UMA 3 eye content to create these examples.");
            // The stock eye material exposes albedo only. Use a separate PBR authoring material
            // for these documents so roughness and iris relief can be edited and exported too.
            var authoring=AssetDatabase.LoadAssetAtPath<UMAMaterial>("Assets/UMA/SRP/ShaderGraphs/Materials/UMA_Diffuse_Normal_Metallic.asset");
            if(authoring==null)throw new InvalidOperationException("Install UMA render pipeline support to create the PBR eye examples.");
            Type storage=typeof(TexturePaintStageWindow).Assembly.GetType("UMA.TexturePaint.Editor.TexturePaintDocumentStorage",true);
            foreach(int look in new[]{0,3})
            {
                string path=Folder+"/UMA Eyes "+(look==0?"Blue":"Hazel")+" Project.asset";
                if(File.Exists(path))continue;
                var launch=new TexturePaintLaunchContext{kind=TexturePaintLaunchKind.StandaloneSlot,selectedSlot=slot,selectedSlotGuid=AssetDatabase.AssetPathToGUID(slotPath),umaMaterial=authoring,
                    umaMaterialGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(authoring)),resolution=1024};
                launch.members.Add(new TexturePaintStandaloneMemberContext{slot=slot,slotGuid=launch.selectedSlotGuid});
                using var controller=new TexturePaintStageController();controller.InitializeStandalone(launch,null,null,null,
                    layerComposite:AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/UMA/OverlayPainter/Shaders/LayerComposite.compute"),
                    channelPack:AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/UMA/OverlayPainter/Shaders/ChannelPack.compute"),resolution:1024,
                    ribbonShader:Shader.Find("Hidden/UMA/TexturePaint/RibbonProjection"));
                var set=controller.Textures.Sets.Single();foreach(var channel in set.channels.Values)channel.editable.Reset(null,Color.clear);
                while(set.layers.Count>0)set.RemoveLayerAt(0);
                storage.GetMethod("AssignStableSurfaceIds",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{controller.Textures});
                using var host=new PluginHost();var plugin=ScriptableObject.CreateInstance<EyeIrisGeneratorPlugin>();
                try
                {
                    var p=host.CreateParameters(plugin);p.Get("look").number=look;
                    await host.ExecuteCommandAsync(plugin,controller.Textures,p,null,CancellationToken.None);
                    var document=(TexturePaintDocument)storage.GetMethod("CreateTransient",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{null,launch});
                    document.hideFlags=HideFlags.None;AssetDatabase.CreateAsset(document,path);document.launchContext=launch;controller.AttachDocument(document);
                    storage.GetMethod("Save",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{document,controller.Textures,false});
                    storage.GetMethod("Restore",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{document,controller.Textures});
                    if(set.layers.Count!=1 || set.layers[0].pluginId!="com.uma.texturepaint.eye-iris" ||
                        !new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl}.All(c=>set.layers[0].channels.ContainsKey(c)))
                        throw new InvalidOperationException("Eye example failed its save/load check: "+path);
                }
                finally{UnityEngine.Object.DestroyImmediate(plugin);}
            }
            AssetDatabase.SaveAssets();return Folder;
        }
    }
}
