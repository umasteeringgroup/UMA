#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintGarmentConstructionTests
    {
        private const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
        private const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
        private static object Invoke(object target,string method,params object[] args)
            =>typeof(TexturePaintStageWindow).GetMethod(method,Instance).Invoke(target,args);
        private static object Call(string method,params object[] args)
            =>typeof(TexturePaintStageWindow).GetMethod(method,Static).Invoke(null,args);
        private static void Set(object target,string field,object value)
            =>typeof(TexturePaintStageWindow).GetField(field,Instance).SetValue(target,value);
        private static T Get<T>(object target,string field)=>(T)typeof(TexturePaintStageWindow).GetField(field,Instance).GetValue(target);

        private sealed class Path:IDisposable
        {
            public readonly TextureSet set=new TextureSet();
            public readonly TexturePaintStageWindow stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            public readonly BrushPreset brush=ScriptableObject.CreateInstance<BrushPreset>();
            public readonly TexturePaintLayer layer;
            public Path(bool hem,bool worldSpace=true)
            {
                layer=set.AddSplineLayer("Construction selection");layer.spline.worldSpace=worldSpace;
                layer.splineSettings=new TexturePaintSplineSettings
                {
                    brushSize=.037f,brushFlow=.63f,brushHardness=.81f,brushRotation=17,brushSpacing=.27f,
                    source=TexturePaintBrushSource.Texture,sourceTexture=Texture2D.whiteTexture,
                    pathMode=TexturePaintPathMode.Ribbon,startFade=.18f,endFade=.23f,sideFadeExtra=.12f,
                    textureFlipX=TexturePaintPathFlipMode.Alternate,textureFlipSeed=91,
                    editorSettingsVersion=1,autoUpdate=false,
                    garment=hem?null:TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.MetalZipper),
                    hemSeam=hem?TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.FlatFelled):null
                };
                layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings=new TexturePaintChannelSourceSettings
                    {source=TexturePaintBrushSource.Texture,sourceTexture=Texture2D.blackTexture,color=Color.cyan};
                Set(stage,"transientBrush",brush);Set(stage,"spline",layer.spline);
                Invoke(stage,"RestoreSplineSettings",layer.splineSettings);
                Set(stage,"pathAutoUpdate",false);
                Invoke(stage,"CaptureSplineSettings",layer);
            }
            public int Select(int index,bool enabled=true,float? width=null)
            {
                var garment=Get<TexturePaintGarmentSettings>(stage,"pathGarment")?.Clone();
                var hem=Get<TexturePaintHemSeamSettings>(stage,"pathHemSeam")?.Clone();
                object[] args={index,enabled,garment,hem};Call("SelectPathConstruction",args);
                Assert.That(Invoke(stage,"ApplyPathConstructionEdit",set,args[2],args[3],width??brush.size*2,index),Is.True);
                return (int)Invoke(stage,"CurrentPathConstruction",layer);
            }
            public void Dispose()
            {
                Invoke(stage,"CancelScheduledSplineReapply");Invoke(stage,"ClearLightweightHistory");
                Object.DestroyImmediate(stage);Object.DestroyImmediate(brush);set.Dispose();
            }
        }

        private static int HemIndex(TexturePaintSeamPreset preset)
            =>Enum.GetValues(typeof(TexturePaintGarmentPreset)).Length+(int)preset;
        private static string UnrelatedSettings(TexturePaintSplineSettings settings)
        {
            var copy=settings.Clone();copy.garment=null;copy.hemSeam=null;copy.hemSeamSelected=false;
            return JsonUtility.ToJson(copy);
        }

        [Test]
        public void ConstructionCatalogIncludesEveryHemOnlyForPathsAndUsesOneGarmentMenu()
        {
            var paths=(string[])Call("BuildGarmentConstructionNames",true);
            var projections=(string[])Call("BuildGarmentConstructionNames",false);
            Assert.That(paths.Length,Is.EqualTo(48));Assert.That(projections.Length,Is.EqualTo(32));
            Assert.That(paths.Distinct().Count(),Is.EqualTo(paths.Length));
            Assert.That(paths.Count(n=>n.StartsWith("Hems & Seams/",StringComparison.Ordinal)),Is.EqualTo(16));
            Assert.That(projections.Any(n=>n.StartsWith("Hems & Seams/",StringComparison.Ordinal)),Is.False);
            using var p=new Path(false);
            var menu=new GenericMenu();Invoke(p.stage,"AddGarmentMenu",menu,p.set,true);
            Assert.That(menu.GetItemCount(),Is.EqualTo(48),"All path constructions must live under the shared Garment menu");
            var projectionMenu=new GenericMenu();Invoke(p.stage,"AddGarmentMenu",projectionMenu,p.set,false);
            Assert.That(projectionMenu.GetItemCount(),Is.EqualTo(32),"Do not offer unsupported hem projections");
        }

        [TestCase(false)] [TestCase(true)]
        public void SwitchingToHemAndBackUsesPathUndoAndPreservesBrushSourcesAndFades(bool worldSpace)
        {
            using var p=new Path(false,worldSpace);
            Get<TexturePaintGarmentSettings>(p.stage,"pathGarment").depthStrength=2.2f;
            Get<TexturePaintGarmentSettings>(p.stage,"pathGarment").shadingStrength=.65f;
            Invoke(p.stage,"CaptureSplineSettings",p.layer);
            string untouched=UnrelatedSettings(p.layer.splineSettings);
            string source=JsonUtility.ToJson(p.layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings);
            int hem=HemIndex(TexturePaintSeamPreset.FrenchSeam);
            Assert.That(p.Select(hem),Is.EqualTo(hem));
            Assert.That(p.layer.splineSettings.hemSeam.enabled,Is.True);
            Assert.That(p.layer.splineSettings.garment.enabled,Is.False);
            Assert.That(p.layer.splineSettings.hemSeam.depthStrength,Is.EqualTo(2.2f));
            Assert.That(p.layer.splineSettings.hemSeam.shadingStrength,Is.EqualTo(.65f));
            Assert.That(UnrelatedSettings(p.layer.splineSettings),Is.EqualTo(untouched));
            Assert.That(JsonUtility.ToJson(p.layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings),Is.EqualTo(source));
            Assert.That(Invoke(p.stage,"UndoLightweight"),Is.True);
            Assert.That(p.layer.splineSettings.garment.enabled,Is.True);
            Assert.That(p.layer.splineSettings.hemSeam,Is.Null);
            Assert.That((int)Invoke(p.stage,"CurrentPathConstruction",p.layer),Is.EqualTo((int)TexturePaintGarmentPreset.MetalZipper));
            Assert.That(Invoke(p.stage,"RedoLightweight"),Is.True);
            Assert.That(p.layer.splineSettings.hemSeam.preset,Is.EqualTo(TexturePaintSeamPreset.FrenchSeam));
            Assert.That((int)Invoke(p.stage,"CurrentPathConstruction",p.layer),Is.EqualTo(hem));
            Assert.That(p.Select((int)TexturePaintGarmentPreset.MetalZipper),Is.EqualTo((int)TexturePaintGarmentPreset.MetalZipper));
            Assert.That(p.layer.splineSettings.garment.enabled,Is.True);
            Assert.That(p.layer.splineSettings.hemSeam.enabled,Is.False);
            Assert.That(UnrelatedSettings(p.layer.splineSettings),Is.EqualTo(untouched));
            Assert.That(JsonUtility.ToJson(p.layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings),Is.EqualTo(source));
        }

        [Test]
        public void OlderHemDocumentsResolveTheirConstructionWithoutActivatingANativeGenerator()
        {
            using var p=new Path(true);
            var loaded=JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(p.layer.splineSettings));
            loaded.hemSeamSelected=false; // Legacy documents do not contain the new preference.
            Invoke(p.stage,"RestoreSplineSettings",loaded);
            Assert.That((int)Invoke(p.stage,"CurrentPathConstruction",p.layer),Is.EqualTo(HemIndex(TexturePaintSeamPreset.FlatFelled)));
            Assert.That(Call("HasGarment",p.layer),Is.True,"Legacy hems share the garment output controls and labeling");
            // Unity serializes inline classes by value and can reconstruct a null field as
            // an inactive settings object. Its enabled state, not null identity, is the contract.
            Assert.That(Get<TexturePaintGarmentSettings>(p.stage,"pathGarment")?.enabled,Is.Not.True);
            var inactiveNative=Get<TexturePaintGarmentSettings>(p.stage,"pathGarment")?.Clone();
            inactiveNative?.Normalize();
            string nativeRecipe=JsonUtility.ToJson(inactiveNative);
            int hem=HemIndex(TexturePaintSeamPreset.FlatFelled);
            Assert.That(p.Select(hem,false),Is.EqualTo(hem),"Disabling generation must retain the selected hem recipe");
            Assert.That(p.layer.splineSettings.hemSeam.enabled,Is.False);
            Assert.That(p.Select(hem),Is.EqualTo(hem));
            Assert.That(p.layer.splineSettings.hemSeam.enabled,Is.True);
            Assert.That(p.layer.splineSettings.garment?.enabled,Is.Not.True);
            Assert.That(JsonUtility.ToJson(p.layer.splineSettings.garment),Is.EqualTo(nativeRecipe),
                "Toggling the loaded hem must leave any inactive native recipe unchanged");
        }

        [TestCase(false)] [TestCase(true)]
        public void HemWidthAndNestedRowsAreEditedAsOneIndependentUndoSnapshot(bool worldSpace)
        {
            using var p=new Path(true,worldSpace);
            var previous=Get<TexturePaintHemSeamSettings>(p.stage,"pathHemSeam");
            var next=previous.Clone();Color original=previous.rows[0].color;next.rows[0].color=Color.magenta;
            float oldWidth=p.brush.size*2;
            Assert.That(Invoke(p.stage,"ApplyPathConstructionEdit",p.set,null,next,.125f,HemIndex(next.preset)),Is.True);
            Assert.That(previous.rows[0].color,Is.EqualTo(original),"Nested row edits must not mutate undo's original recipe");
            Assert.That(p.layer.splineSettings.brushSize,Is.EqualTo(.0625f));
            Assert.That(p.layer.splineSettings.hemSeam.rows[0].color,Is.EqualTo(Color.magenta));
            Assert.That(Invoke(p.stage,"UndoLightweight"),Is.True);
            Assert.That(p.brush.size*2,Is.EqualTo(oldWidth));
            Assert.That(p.layer.splineSettings.hemSeam.rows[0].color,Is.EqualTo(original));
            Assert.That(Invoke(p.stage,"RedoLightweight"),Is.True);
            Assert.That(p.brush.size,Is.EqualTo(.0625f));
            Assert.That(p.layer.splineSettings.hemSeam.rows[0].color,Is.EqualTo(Color.magenta));
        }

        [Test]
        public void DisabledConstructionSelectionHasUndoEvenWhenBothStoredRecipesStayUnchanged()
        {
            using var p=new Path(true);
            int native=(int)TexturePaintGarmentPreset.MetalZipper,hem=HemIndex(TexturePaintSeamPreset.FlatFelled);
            p.Select(native,false);
            Invoke(p.stage,"ClearLightweightHistory");
            string garmentRecipe=JsonUtility.ToJson(p.layer.splineSettings.garment);
            string hemRecipe=JsonUtility.ToJson(p.layer.splineSettings.hemSeam);
            Assert.That(p.Select(hem,false),Is.EqualTo(hem));
            Assert.That(JsonUtility.ToJson(p.layer.splineSettings.garment),Is.EqualTo(garmentRecipe));
            Assert.That(JsonUtility.ToJson(p.layer.splineSettings.hemSeam),Is.EqualTo(hemRecipe));
            Assert.That(p.layer.splineSettings.hemSeamSelected,Is.True);
            Assert.That(Invoke(p.stage,"UndoLightweight"),Is.True);
            Assert.That((int)Invoke(p.stage,"CurrentPathConstruction",p.layer),Is.EqualTo(native));
            Assert.That(p.layer.splineSettings.hemSeamSelected,Is.False);
            Assert.That(Invoke(p.stage,"RedoLightweight"),Is.True);
            Assert.That((int)Invoke(p.stage,"CurrentPathConstruction",p.layer),Is.EqualTo(hem));
            Assert.That(p.layer.splineSettings.hemSeamSelected,Is.True);
            Assert.That(p.layer.splineSettings.garment.enabled||p.layer.splineSettings.hemSeam.enabled,Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void DisabledConstructionSelectionSurvivesSaveAndRestoreIntoANewEditor(bool hemSelected)
        {
            using var p=new Path(true);
            int native=(int)TexturePaintGarmentPreset.MetalZipper;
            int hem=HemIndex(TexturePaintSeamPreset.FlatFelled);
            p.Select(native,false); // Retain both recipes with generation disabled.
            if(hemSelected)p.Select(hem,false);
            var saved=JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(p.layer.splineSettings));
            Assert.That(saved.hemSeamSelected,Is.EqualTo(hemSelected));
            Assert.That(saved.garment.enabled||saved.hemSeam.enabled,Is.False);
            using var reopened=new Path(!hemSelected);
            reopened.layer.splineSettings=saved;
            Invoke(reopened.stage,"RestoreSplineSettings",saved);
            int expected=hemSelected?hem:native;
            Assert.That((int)Invoke(reopened.stage,"CurrentPathConstruction",reopened.layer),Is.EqualTo(expected));
            Invoke(reopened.stage,"CaptureSplineSettings",reopened.layer);
            Assert.That(reopened.layer.splineSettings.hemSeamSelected,Is.EqualTo(hemSelected));
            reopened.Select(expected);
            Assert.That(reopened.layer.splineSettings.hemSeam.enabled,Is.EqualTo(hemSelected));
            Assert.That(reopened.layer.splineSettings.garment.enabled,Is.EqualTo(!hemSelected));
        }

        [Test]
        public void ActiveNativeGeneratorTakesPrecedenceOverAStaleHemPreference()
        {
            using var p=new Path(false);
            p.layer.splineSettings.hemSeam=TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.FlatFelled);
            p.layer.splineSettings.hemSeamSelected=true;
            Invoke(p.stage,"RestoreSplineSettings",p.layer.splineSettings);
            Assert.That((int)Invoke(p.stage,"CurrentPathConstruction",p.layer),Is.EqualTo((int)TexturePaintGarmentPreset.MetalZipper));
            Invoke(p.stage,"CaptureSplineSettings",p.layer);
            Assert.That(p.layer.splineSettings.hemSeamSelected,Is.False);
        }

        [Test]
        public void LinkedPathInstanceCannotReplaceItsConstructionThroughTheSharedEditor()
        {
            using var p=new Path(true);
            p.layer.links=new TexturePaintLayerLinks{instance=new TexturePaintLayerReference{layerId="source"}};
            string before=JsonUtility.ToJson(p.layer.splineSettings);
            Assert.That(Invoke(p.stage,"ApplyPathConstructionEdit",p.set,
                TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.MetalZipper),null,.2f,
                (int)TexturePaintGarmentPreset.MetalZipper),Is.False);
            Assert.That(JsonUtility.ToJson(p.layer.splineSettings),Is.EqualTo(before));
        }

        [Test]
        public void SharedGeneratorToggleEditsTheLoadedHemInTheActualIMGUI()
        {
            using var p=new Path(true);
            var window=ScriptableObject.CreateInstance<TexturePaintConstructionInputTestWindow>();
            bool focus=true;string focused=null;
            window.draw=()=>
            {
                Invoke(p.stage,"DrawPathGarmentProperties",p.set);
                if(focus)GUI.FocusControl("GarmentGeneratorEnabled");
                focused=GUI.GetNameOfFocusedControl();
            };
            try
            {
                window.position=new Rect(100,100,520,1000);window.wantsMouseMove=true;window.ShowUtility();window.Focus();
                window.SendEvent(new Event{type=EventType.Layout});
                window.SendEvent(new Event{type=EventType.Repaint});
                window.SendEvent(new Event{type=EventType.MouseMove,mousePosition=new Vector2(400,10)});
                Assert.That(focused,Is.EqualTo("GarmentGeneratorEnabled"));
                focus=false;
                window.SendEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Space,character=' '});
                window.SendEvent(new Event{type=EventType.KeyUp,keyCode=KeyCode.Space});
                Assert.That(p.layer.splineSettings.hemSeam.enabled,Is.False,"The single header toggle must control the selected legacy hem");
                Assert.That(p.layer.splineSettings.garment,Is.Null,"Drawing hem controls must not create or enable a competing native generator");
                Assert.That(Invoke(p.stage,"UndoLightweight"),Is.True);
                Assert.That(p.layer.splineSettings.hemSeam.enabled,Is.True);
            }
            finally{window.draw=null;window.Close();}
        }
    }

    internal sealed class TexturePaintConstructionInputTestWindow:EditorWindow
    {
        internal Action draw;
        private void OnGUI()=>draw?.Invoke();
    }
}
#endif
