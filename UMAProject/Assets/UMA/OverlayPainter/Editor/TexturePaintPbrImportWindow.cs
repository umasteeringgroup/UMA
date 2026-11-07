using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    public enum TexturePaintPbrMap { Ignore, Albedo, Normal, Metallic, Roughness, Smoothness, AmbientOcclusion, Height, Emission, ORM, RMA, UnityMask }

    public static class TexturePaintPbrImport
    {
        public static TexturePaintPbrMap Guess(string filename)
        {
            string name=Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
            string[] words=Regex.Split(name,"[^a-z0-9]+");
            bool Has(params string[] values)=>values.Any(v=>words.Contains(v)||name.EndsWith(v,StringComparison.Ordinal));
            if(Has("orm","arm","occlusionroughnessmetallic"))return TexturePaintPbrMap.ORM;
            if(Has("rma","roughnessmetallicao"))return TexturePaintPbrMap.RMA;
            if(Has("maskmap","unitymask"))return TexturePaintPbrMap.UnityMask;
            if(Has("normal","normalgl","normaldx","nor","nrm","nor_gl","nor_dx"))return TexturePaintPbrMap.Normal;
            if(Has("roughness","rough"))return TexturePaintPbrMap.Roughness;
            if(Has("smoothness","smooth","gloss","glossiness"))return TexturePaintPbrMap.Smoothness;
            if(Has("metallic","metalness","metal"))return TexturePaintPbrMap.Metallic;
            if(Has("ao","ambientocclusion","occlusion"))return TexturePaintPbrMap.AmbientOcclusion;
            if(Has("height","displacement","disp"))return TexturePaintPbrMap.Height;
            if(Has("emission","emissive"))return TexturePaintPbrMap.Emission;
            if(Has("albedo","basecolor","base_color","diffuse","color","colour"))return TexturePaintPbrMap.Albedo;
            return TexturePaintPbrMap.Ignore;
        }

        public static IEnumerable<(TexturePaintChannel channel,int component,bool invert)> Outputs(TexturePaintPbrMap map)
        {
            if(map==TexturePaintPbrMap.ORM) {yield return(TexturePaintChannel.AmbientOcclusion,0,false);yield return(TexturePaintChannel.Roughness,1,false);yield return(TexturePaintChannel.Metallic,2,false);}
            else if(map==TexturePaintPbrMap.RMA) {yield return(TexturePaintChannel.Roughness,0,false);yield return(TexturePaintChannel.Metallic,1,false);yield return(TexturePaintChannel.AmbientOcclusion,2,false);}
            else if(map==TexturePaintPbrMap.UnityMask) {yield return(TexturePaintChannel.Metallic,0,false);yield return(TexturePaintChannel.AmbientOcclusion,1,false);yield return(TexturePaintChannel.Roughness,3,true);}
            else if(map!=TexturePaintPbrMap.Ignore)
            {
                TexturePaintChannel channel=map switch
                {
                    TexturePaintPbrMap.Albedo=>TexturePaintChannel.Albedo,TexturePaintPbrMap.Normal=>TexturePaintChannel.Normal,
                    TexturePaintPbrMap.Metallic=>TexturePaintChannel.Metallic,TexturePaintPbrMap.AmbientOcclusion=>TexturePaintChannel.AmbientOcclusion,
                    TexturePaintPbrMap.Height=>TexturePaintChannel.NormalControl,TexturePaintPbrMap.Emission=>TexturePaintChannel.Emission,
                    _=>TexturePaintChannel.Roughness
                };
                yield return(channel,channel==TexturePaintChannel.Albedo||channel==TexturePaintChannel.Normal||channel==TexturePaintChannel.Emission?-1:0,map==TexturePaintPbrMap.Smoothness);
            }
        }

        public static Color ConvertPixel(Color pixel,TexturePaintChannel channel,int component,bool invert,bool directX,float coverage)
        {
            if(component>=0){float v=pixel[component];if(invert)v=1-v;return new Color(v,v,v,coverage);}
            if(channel==TexturePaintChannel.Normal&&directX)pixel.g=1-pixel.g;
            pixel.a=coverage;return pixel;
        }
    }

    public sealed class TexturePaintPbrImportWindow : EditorWindow
    {
        private sealed class Entry { public string path;public TexturePaintPbrMap map; }
        private readonly List<Entry> entries=new();
        private ScrollView rows;
        private Label status;
        private Toggle directX;
        public string LastStatus { get; private set; }

        public void SetSources(IEnumerable<KeyValuePair<string,TexturePaintPbrMap>> sources)
        {
            entries.Clear();
            foreach(var source in sources)entries.Add(new Entry{path=source.Key,map=source.Value});
        }
        private void Report(string message){LastStatus=message;if(status!=null)status.text=message;}

        [MenuItem("UMA/Overlay Painter/Import PBR Texture Set...")]
        public static void Open()=>GetWindow<TexturePaintPbrImportWindow>("Import PBR Texture Set");

        public void CreateGUI()
        {
            minSize=new Vector2(540,400);var root=rootVisualElement;
            root.style.paddingLeft=10;root.style.paddingRight=10;root.style.paddingTop=8;
            root.Add(new HelpBox("Choose one material's texture folder, review the proposed channels, then create a Sprite Set and reusable Fill Material Preset. Source files remain unchanged. ORM = R:AO G:Roughness B:Metallic; RMA = R:Roughness G:Metallic B:AO.",HelpBoxMessageType.Info));
            root.Add(new Button(ChooseFolder){text="Choose Texture Folder..."});
            directX=new Toggle("Source normals use DirectX (flip green)");root.Add(directX);
            rows=new ScrollView();rows.style.flexGrow=1;root.Add(rows);
            status=new Label();status.style.whiteSpace=WhiteSpace.Normal;root.Add(status);
            root.Add(new Button(Import){text="Create Sprite Set and Material Preset..."});
        }

        private void ChooseFolder()
        {
            string folder=EditorUtility.OpenFolderPanel("Choose one PBR material folder","","");if(string.IsNullOrEmpty(folder))return;
            entries.Clear();rows.Clear();
            foreach(string path in Directory.GetFiles(folder).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase))
            {
                if(!new[]{".png",".jpg",".jpeg",".tga",".tif",".tiff",".exr",".psd"}.Contains(Path.GetExtension(path).ToLowerInvariant()))continue;
                var entry=new Entry{path=path,map=TexturePaintPbrImport.Guess(path)};entries.Add(entry);
                var row=new EnumField(Path.GetFileName(path),entry.map);
                row.RegisterValueChangedCallback(evt=>entry.map=(TexturePaintPbrMap)evt.newValue);rows.Add(row);
            }
            directX.value=entries.Any(e=>e.map==TexturePaintPbrMap.Normal&&Regex.IsMatch(Path.GetFileNameWithoutExtension(e.path),"(directx|normaldx|nor_dx)",RegexOptions.IgnoreCase));
            status.text=$"{entries.Count} textures found. Resolve duplicate channels by setting unused files to Ignore.";
        }

        private void Import()
        {
            string destination=EditorUtility.SaveFilePanelInProject("Save PBR Sprite Set","PBR Material","asset","Create a new Sprite Set and matching material preset.");
            if(!string.IsNullOrEmpty(destination))ImportTo(destination,directX.value);
        }

        /// <summary>Also available to batch tooling; uses exactly the reviewed UI import path.</summary>
        public bool ImportTo(string destination,bool sourceDirectX)
        {
            var selected=entries.Where(e=>e.map!=TexturePaintPbrMap.Ignore).ToList();
            if(selected.Count==0){Report("Choose and assign at least one texture.");return false;}
            var duplicates=selected.SelectMany(e=>TexturePaintPbrImport.Outputs(e.map)).GroupBy(o=>o.channel).Where(g=>g.Count()>1).ToList();
            if(duplicates.Count>0){Report("Duplicate channels: "+string.Join(", ",duplicates.Select(d=>d.Key))+". Choose which maps to use.");return false;}
            if(string.IsNullOrEmpty(destination)||!destination.StartsWith("Assets/",StringComparison.Ordinal)||
                !Path.GetFullPath(destination).StartsWith(Path.GetFullPath(Application.dataPath)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||
                !destination.EndsWith(".asset",StringComparison.OrdinalIgnoreCase))
            {Report("Choose an .asset destination inside this project's Assets folder.");return false;}
            if(File.Exists(destination)){Report("Choose a new filename; existing assets are preserved.");return false;}
            string parent=Path.GetDirectoryName(destination).Replace('\\','/'),name=Path.GetFileNameWithoutExtension(destination);
            string mapFolder=AssetDatabase.GenerateUniqueAssetPath(parent+"/"+name+" Maps");
            var created=new List<string>();var textures=new Dictionary<Entry,Texture2D>();
            var set=CreateInstance<OverlayPainterSpriteSet>();var preset=CreateInstance<TexturePaintMaterialPreset>();
            try
            {
                AssetDatabase.CreateFolder(parent,Path.GetFileName(mapFolder));created.Add(mapFolder);
                foreach(var entry in selected)
                {
                    if(EditorUtility.DisplayCancelableProgressBar("PBR Texture Import",Path.GetFileName(entry.path),textures.Count/(float)selected.Count))throw new OperationCanceledException();
                    string path=AssetDatabase.GenerateUniqueAssetPath(mapFolder+"/Source-"+Path.GetFileName(entry.path));
                    File.Copy(entry.path,path,false);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                    importer.textureType=TextureImporterType.Default;importer.sRGBTexture=entry.map==TexturePaintPbrMap.Albedo||entry.map==TexturePaintPbrMap.Emission;
                    importer.maxTextureSize=16384;importer.npotScale=TextureImporterNPOTScale.None;
                    importer.isReadable=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.alphaIsTransparency=false;importer.SaveAndReimport();
                    textures.Add(entry,AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                }
                Texture2D albedo=selected.Where(e=>e.map==TexturePaintPbrMap.Albedo).Select(e=>textures[e]).FirstOrDefault();
                var layer=new TexturePaintDocumentLayer{name=name,kind=TexturePaintLayerKind.Fill,fillChannel=TexturePaintChannel.Albedo,fillSettings=new TexturePaintFillSettings()};
                set.setName=name;set.spriteNames.Add(name);
                foreach(var entry in selected)
                {
                    Texture2D input=textures[entry];Color[] source=input.GetPixels();
                    foreach(var output in TexturePaintPbrImport.Outputs(entry.map))
                    {
                        var pixels=new Color[source.Length];
                        for(int y=0;y<input.height;y++)for(int x=0;x<input.width;x++)
                        {
                            int i=y*input.width+x;
                            float coverage=albedo!=null?albedo.GetPixelBilinear((x+.5f)/input.width,(y+.5f)/input.height).a:1;
                            pixels[i]=TexturePaintPbrImport.ConvertPixel(source[i],output.channel,output.component,output.invert,sourceDirectX,coverage);
                        }
                        // EXR preserves numeric precision for imported height. Other outputs are
                        // conventional PNG material maps, with the albedo's coverage on every map.
                        bool height=output.channel==TexturePaintChannel.NormalControl;
                        var texture=new Texture2D(input.width,input.height,height?TextureFormat.RGBAFloat:TextureFormat.RGBA32,false,true);
                        string path=mapFolder+"/"+output.channel+(height?".exr":".png");
                        try {texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(path,height?texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat):texture.EncodeToPNG());}
                        finally{DestroyImmediate(texture);}
                        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                        importer.maxTextureSize=16384;importer.npotScale=TextureImporterNPOTScale.None;
                        importer.sRGBTexture=output.channel==TexturePaintChannel.Albedo||output.channel==TexturePaintChannel.Emission;
                        importer.alphaIsTransparency=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.wrapMode=TextureWrapMode.Repeat;importer.SaveAndReimport();
                        var imported=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        set.spriteSheets.Add(new OverlayPainterSpriteSheet{channel=output.channel,spriteSheet=imported});
                        var saved=new TexturePaintDocumentLayerChannel{channel=output.channel};
                        saved.settings.channel=output.channel;saved.settings.enabled=true;saved.settings.opacity=1;
                        if(height)saved.settings.blendMode=TexturePaintBlendMode.Overlay;
                        saved.SetSourceSettings(new TexturePaintChannelSourceSettings{source=TexturePaintBrushSource.Texture,sourceTexture=imported,color=Color.white});
                        layer.channels.Add(saved);preset.channels.Add(new TexturePaintMaterialPresetChannel{channel=output.channel,required=true});
                    }
                }
                // Keep albedo readable until every output has sampled its shared coverage.
                foreach(var input in textures.Values)
                {var sourceImporter=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(input));sourceImporter.isReadable=false;sourceImporter.SaveAndReimport();}
                preset.displayName=name;preset.category="Imported PBR";preset.description="Imported PBR material with reviewed channel assignments and shared alpha coverage.";
                preset.layers.Add(layer);preset.includesWholeStack=false;preset.includesCachedPluginOutput=false;
                AssetDatabase.CreateAsset(set,destination);created.Add(destination);
                string presetPath=AssetDatabase.GenerateUniqueAssetPath(parent+"/"+name+" Material.asset");AssetDatabase.CreateAsset(preset,presetPath);created.Add(presetPath);
                AssetDatabase.SaveAssets();Selection.activeObject=preset;EditorGUIUtility.PingObject(preset);
                Report("Created "+destination+" and "+presetPath+". Apply the preset from Material Presets or use the Sprite Set.");
                return true;
            }
            catch(Exception exception)
            {
                for(int i=created.Count-1;i>=0;i--)AssetDatabase.DeleteAsset(created[i]);
                if(!AssetDatabase.Contains(set))DestroyImmediate(set);if(!AssetDatabase.Contains(preset))DestroyImmediate(preset);
                Report(exception is OperationCanceledException?"Import canceled; no partial material was retained.":exception.Message);
                return false;
            }
            finally {EditorUtility.ClearProgressBar();}
        }
    }
}
