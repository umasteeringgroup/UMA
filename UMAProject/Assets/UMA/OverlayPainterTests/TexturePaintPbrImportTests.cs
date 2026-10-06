#if UNITY_INCLUDE_TESTS
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPbrImportTests
    {
        [Test]
        public void FolderImportCreatesReusableAssetsWithSharedAlphaAndPreservesSources()
        {
            string folder="Assets/__PbrImportTest_"+Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
            var window=ScriptableObject.CreateInstance<TexturePaintPbrImportWindow>();
            var previous=Selection.activeObject;
            try
            {
                string albedo=Write("cloth_albedo.png",new Color(.25f,.5f,.75f,.4f));
                string orm=Write("cloth_orm.png",new Color(.8f,.65f,.3f,.1f));
                string normal=Write("cloth_normaldx.png",new Color(.5f,.8f,.9f,1));
                byte[] original=File.ReadAllBytes(albedo);
                window.SetSources(new Dictionary<string,TexturePaintPbrMap>{{albedo,TexturePaintPbrMap.Albedo},{orm,TexturePaintPbrMap.ORM},{normal,TexturePaintPbrMap.Normal}});
                Assert.That(window.ImportTo(folder+"/Imported.asset",true),Is.True,window.LastStatus);
                Assert.That(File.ReadAllBytes(albedo),Is.EqualTo(original));
                var set=AssetDatabase.LoadAssetAtPath<OverlayPainterSpriteSet>(folder+"/Imported.asset");
                var preset=AssetDatabase.LoadAssetAtPath<TexturePaintMaterialPreset>(folder+"/Imported Material.asset");
                Assert.That(set.spriteSheets.Count,Is.EqualTo(5));Assert.That(preset.layers.Single().channels.Count,Is.EqualTo(5));
                foreach(var sheet in set.spriteSheets)
                {
                    string path=AssetDatabase.GetAssetPath(sheet.spriteSheet);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                    Assert.That(importer.sRGBTexture,Is.EqualTo(sheet.channel==TexturePaintChannel.Albedo));
                    Assert.That(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count(),Is.EqualTo(1));
                    importer.isReadable=true;importer.SaveAndReimport();
                    Color pixel=AssetDatabase.LoadAssetAtPath<Texture2D>(path).GetPixel(0,0);
                    Assert.That(pixel.a,Is.EqualTo(.4f).Within(1f/255),sheet.channel.ToString());
                    if(sheet.channel==TexturePaintChannel.Roughness)Assert.That(pixel.r,Is.EqualTo(.65f).Within(1f/255));
                    if(sheet.channel==TexturePaintChannel.Normal)Assert.That(pixel.g,Is.EqualTo(.2f).Within(1f/255));
                    if(sheet.channel==TexturePaintChannel.Albedo)Assert.That(pixel.r,Is.EqualTo(.25f).Within(1f/255),"No extra gamma conversion during import.");
                }
                Assert.That(preset.layers.Single().channels.All(c=>c.settings.enabled&&c.settings.opacity==1),Is.True);
                Assert.That(window.ImportTo(folder+"/Imported.asset",false),Is.False,"Existing assets must not be overwritten.");
            }
            finally {Selection.activeObject=previous;UnityEngine.Object.DestroyImmediate(window);AssetDatabase.DeleteAsset(folder);}

            string Write(string name,Color color)
            {
                var texture=new Texture2D(16,16,TextureFormat.RGBA32,false);
                try{texture.SetPixels(Enumerable.Repeat(color,256).ToArray());texture.Apply();string path=folder+"/"+name;File.WriteAllBytes(path,texture.EncodeToPNG());return path;}
                finally{UnityEngine.Object.DestroyImmediate(texture);}
            }
        }
        [TestCase("Leather_BaseColor.png",TexturePaintPbrMap.Albedo)]
        [TestCase("Leather_NormalDX.png",TexturePaintPbrMap.Normal)]
        [TestCase("Leather_ORM.png",TexturePaintPbrMap.ORM)]
        [TestCase("Leather_RMA.png",TexturePaintPbrMap.RMA)]
        [TestCase("Leather_MaskMap.png",TexturePaintPbrMap.UnityMask)]
        [TestCase("Leather_Glossiness.png",TexturePaintPbrMap.Smoothness)]
        [TestCase("Leather_Preview.png",TexturePaintPbrMap.Ignore)]
        public void ProposesKnownMapsAndLeavesUnknownFilesUnassigned(string filename,TexturePaintPbrMap expected)
        {Assert.That(TexturePaintPbrImport.Guess(filename),Is.EqualTo(expected));}

        [Test] public void UnityMaskExtractsSmoothnessAlphaWithoutTreatingItAsCoverage()
        {
            var output=TexturePaintPbrImport.Outputs(TexturePaintPbrMap.UnityMask).Single(o=>o.channel==TexturePaintChannel.Roughness);
            var pixel=TexturePaintPbrImport.ConvertPixel(new Color(.2f,.3f,.4f,.9f),output.channel,output.component,output.invert,false,.6f);
            Assert.That(pixel.r,Is.EqualTo(.1f).Within(.0001f));Assert.That(pixel.a,Is.EqualTo(.6f));
        }
        [Test] public void DirectXConversionFlipsGreenAndUsesAlbedoCoverage()
        {
            var pixel=TexturePaintPbrImport.ConvertPixel(new Color(.7f,.8f,1,.2f),TexturePaintChannel.Normal,-1,false,true,.45f);
            Assert.That(pixel.g,Is.EqualTo(.2f).Within(.0001f));Assert.That(pixel.r,Is.EqualTo(.7f));Assert.That(pixel.a,Is.EqualTo(.45f));
        }
    }
}
#endif
