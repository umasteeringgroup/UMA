using System;
using System.IO;
using UMA.TexturePaint.Examples;
using UnityEngine;
using UnityEditor;
namespace UMA.TexturePaint.Examples.Editor
{
    public static class SurfaceDetailExamples
    {
        public const string Folder="Assets/UMA/OverlayPainterExamples/Surface Details";
        [MenuItem("UMA/Overlay Painter/Examples/Create Surface Detail Presets")]
        public static void Create()
        {
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            for(int family=0;family<2;family++)
            {
                bool splash=family==1;
                var plugin=splash?(SurfaceDepositionPlugin)ScriptableObject.CreateInstance<SplashDepositionGeneratorPlugin>():ScriptableObject.CreateInstance<SurfaceResidueGeneratorPlugin>();
                try
                {
                    string[] names=splash?new[]{"Fresh Blood","Dried Mud","Blue Paint","Oily Runoff"}:new[]{"Oily Fingerprints","Wiped Smudges","Dried Water Rings","Salt Crust","Peeled Adhesive"};
                    for(int style=0;style<names.Length;style++)
                    {
                        string path=Folder+"/"+names[style]+".asset";if(File.Exists(path))continue;
                        var p=Parameters(plugin,style,splash);
                        var asset=ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();asset.displayName=names[style];asset.category="Surface Details";asset.author="UMA";
                        asset.description=splash?"Editable structured deposition. Select World XY/ZY for world gravity; masks restrict the receiver.":"Roughness-led residue. Optional color and relief remain adjustable.";
                        asset.portability=TexturePaintPresetPortability.RequiresPlugin|TexturePaintPresetPortability.UVDependent;
                        var d=plugin.Descriptor;var layer=new TexturePaintDocumentLayer{name=names[style],kind=TexturePaintLayerKind.Plugin,pluginId=d.id,pluginVersion=d.pluginVersion,pluginParameters=p,pluginParametersJson=JsonUtility.ToJson(p)};
                        foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.Metallic,TexturePaintChannel.NormalControl})
                        {
                            if(!p.Boolean("output"+channel))continue;
                            var c=new TexturePaintDocumentLayerChannel{channel=channel};c.settings.channel=channel;
                            c.settings.blendMode=channel==TexturePaintChannel.NormalControl?TexturePaintBlendMode.Overlay:TexturePaintBlendMode.Normal;
                            layer.channels.Add(c);asset.channels.Add(new TexturePaintMaterialPresetChannel{channel=channel,required=true});
                        }
                        asset.layers.Add(layer);asset.plugins.Add(new TexturePaintMaterialPresetPlugin{pluginId=d.id,pluginVersion=d.pluginVersion,apiVersion=d.apiVersion,declaredChannels=d.declaredChannels,readChannels=d.readChannels,targets=d.supportedTargets});
                        AssetDatabase.CreateAsset(asset,path);
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(plugin);}
            }
            foreach(var preset in new[]{TexturePaintGarmentPreset.TigWeld,TexturePaintGarmentPreset.MigWeld,TexturePaintGarmentPreset.SolderSeam,TexturePaintGarmentPreset.BrazedSeam,TexturePaintGarmentPreset.FloralLace,TexturePaintGarmentPreset.GothicLace,TexturePaintGarmentPreset.ScallopedLace})
            {
                string path=Folder+"/"+preset+".asset";if(File.Exists(path))continue;
                var asset=ScriptableObject.CreateInstance<TexturePaintGarmentPresetAsset>();asset.settings=TexturePaintGarmentSettings.Create(preset);AssetDatabase.CreateAsset(asset,path);
            }
            AssetDatabase.SaveAssets();
        }
        public static TexturePaintPluginParameterSet Parameters(SurfaceDepositionPlugin plugin,int style,bool splash)
        {
            var p=new TexturePaintPluginParameterSet();p.ResetToDefaults(plugin.Descriptor);p.Get("style").number=style;
            if(splash)
            {
                p.Get("drying").number=style==1?.9f:style==3?.05f:.15f;
                p.Get("gravity").number=style==1?.55f:1.15f;
                p.Get("roughness").number=style==3?.07f:.14f;
                p.Get("relief").number=style==1?.018f:style==2?.008f:style==3?.003f:.006f;
            }
            else if(style>=2)
            {
                p.Get("outputAlbedo").boolean=true;p.Get("outputNormalControl").boolean=true;
                p.Get("roughness").number=style==3?.88f:style==2?.6f:.4f;
                p.Get("relief").number=style==3?.025f:.005f;
            }
            return p;
        }
    }
}
