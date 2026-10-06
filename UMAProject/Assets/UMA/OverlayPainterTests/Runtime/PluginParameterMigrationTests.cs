#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed partial class PluginApiV2Tests
    {
        private sealed class DefaultHydrationPlugin : ITexturePaintGeneratorV2
        {
            public TexturePaintPluginParameterSet received;
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor
            {
                id="com.uma.tests.default-hydration", displayName="Default hydration",
                capabilities=TexturePaintPluginCapability.Generator,
                declaredChannels=TexturePaintChannelMask.Albedo,
                parameters=new List<TexturePaintPluginParameterDefinition>
                {
                    new TexturePaintPluginParameterDefinition {id="section",type=TexturePaintPluginParameterType.Header},
                    new TexturePaintPluginParameterDefinition {id="amount",type=TexturePaintPluginParameterType.Float,minimum=0,maximum=1,defaultNumber=.7f},
                    new TexturePaintPluginParameterDefinition {id="depth",type=TexturePaintPluginParameterType.Float,minimum=0,maximum=4,defaultNumber=1},
                    new TexturePaintPluginParameterDefinition {id="enabled",type=TexturePaintPluginParameterType.Boolean,defaultBoolean=true},
                    new TexturePaintPluginParameterDefinition {id="tint",type=TexturePaintPluginParameterType.Color,defaultColor=Color.cyan},
                    new TexturePaintPluginParameterDefinition {id="text",type=TexturePaintPluginParameterType.String,defaultText="Default"},
                    new TexturePaintPluginParameterDefinition {id="texture",type=TexturePaintPluginParameterType.Texture},
                    new TexturePaintPluginParameterDefinition {id="curve",type=TexturePaintPluginParameterType.Curve,
                        defaultCurve=AnimationCurve.Linear(0,.2f,1,.8f)},
                    new TexturePaintPluginParameterDefinition {id="stripes",type=TexturePaintPluginParameterType.StripeList,
                        defaultStripes=new List<TexturePaintStripeDefinition>{new TexturePaintStripeDefinition{width=.17f,color=Color.red}}}
                }
            };
            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                received=context.parameters.Clone();
                return Task.CompletedTask;
            }
        }

        [Test]
        public void MissingDefaultsPreserveAuthoredZeroesColorsTexturesAndEmptyLists()
        {
            var plugin=new DefaultHydrationPlugin();
            var parameters=new TexturePaintPluginParameterSet();
            var texture=new Texture2D(1,1);
            try
            {
                parameters.Get("amount",true).number=0;
                parameters.Get("enabled",true).boolean=false;
                parameters.Get("tint",true).color=Color.clear;
                parameters.Get("text",true).text=string.Empty;
                parameters.Get("texture",true).texture=texture;
                var curve=AnimationCurve.Constant(0,1,.37f);
                parameters.Get("curve",true).curve=curve;
                var stripes=parameters.Stripes("stripes");
                parameters.Get("future-parameter",true).number=42;
                Assert.That(parameters.EnsureDefaults(plugin.Descriptor),Is.True);
                Assert.That(parameters.Float("depth"),Is.EqualTo(1));
                Assert.That(parameters.Float("amount"),Is.Zero);
                Assert.That(parameters.Boolean("enabled"),Is.False);
                Assert.That(parameters.Color("tint",Color.white),Is.EqualTo(Color.clear));
                Assert.That(parameters.String("text"),Is.Empty);
                Assert.That(parameters.Texture("texture"),Is.SameAs(texture));
                Assert.That(parameters.Curve("curve"),Is.SameAs(curve));
                Assert.That(parameters.Stripes("stripes"),Is.SameAs(stripes).And.Empty);
                Assert.That(parameters.Float("future-parameter"),Is.EqualTo(42));
                Assert.That(parameters.Get("section"),Is.Null);
                int count=parameters.values.Count;
                Assert.That(parameters.EnsureDefaults(plugin.Descriptor),Is.False);
                Assert.That(parameters.values,Has.Count.EqualTo(count));
                var saved=JsonUtility.FromJson<TexturePaintPluginParameterSet>(JsonUtility.ToJson(parameters));
                saved.EnsureDefaults(plugin.Descriptor);
                Assert.That(saved.Float("amount"),Is.Zero);
                Assert.That(saved.Color("tint",Color.white),Is.EqualTo(Color.clear));
                Assert.That(saved.Stripes("stripes"),Is.Empty);
            }
            finally{Object.DestroyImmediate(texture);}
        }

        [Test]
        public void NewlyAddedCurveAndStripeDefaultsAreIndependentBetweenLayersAndSchema()
        {
            var plugin=new DefaultHydrationPlugin();
            var first=new TexturePaintPluginParameterSet();var second=new TexturePaintPluginParameterSet();
            first.EnsureDefaults(plugin.Descriptor);second.EnsureDefaults(plugin.Descriptor);
            first.Curve("curve").MoveKey(0,new Keyframe(0,.9f));
            first.Stripes("stripes")[0].width=.9f;
            Assert.That(second.Curve("curve").Evaluate(0),Is.EqualTo(.2f).Within(.0001f));
            Assert.That(second.Stripes("stripes")[0].width,Is.EqualTo(.17f));
            Assert.That(plugin.Descriptor.parameters.Find(p=>p.id=="curve").defaultCurve.Evaluate(0),Is.EqualTo(.2f).Within(.0001f));
            Assert.That(plugin.Descriptor.parameters.Find(p=>p.id=="stripes").defaultStripes[0].width,Is.EqualTo(.17f));
        }

        [Test]
        public void EditingAnOldPluginLayerHydratesItsCloneWithoutMutatingSavedParameters()
        {
            var plugin=new DefaultHydrationPlugin();
            var layer=new TexturePaintLayer{kind=TexturePaintLayerKind.Plugin,pluginId=plugin.Descriptor.id};
            layer.pluginParameters.Get("amount",true).number=0;
            string before=JsonUtility.ToJson(layer.pluginParameters);
            var editing=host.GetLayerParameters(layer,plugin);
            Assert.That(editing.Float("depth"),Is.EqualTo(1));
            Assert.That(editing.Boolean("enabled"),Is.True);
            Assert.That(editing.Color("tint",Color.white),Is.EqualTo(Color.cyan));
            Assert.That(editing.Float("amount"),Is.Zero);
            editing.Stripes("stripes")[0].width=.5f;
            Assert.That(JsonUtility.ToJson(layer.pluginParameters),Is.EqualTo(before),
                "Drawing the inspector must not dirty the saved layer or bypass its Undo path");
        }

        [Test]
        public void RestoringOldPluginProfilesMergesNewDefaultsWithoutResettingAuthoredValues()
        {
            var plugin=new DefaultHydrationPlugin();host.GetParameters(plugin);
            var old=new TexturePaintPluginParameterSet();old.Get("amount",true).number=0;
            host.RestoreProfiles(new[]{new TexturePaintPluginProfile{pluginId=plugin.Descriptor.id,parameters=old}});
            var current=host.GetParameters(plugin);
            Assert.That(current.Float("amount"),Is.Zero);
            Assert.That(current.Float("depth"),Is.EqualTo(1));
            Assert.That(current.Boolean("enabled"),Is.True);
            Assert.That(old.Get("depth"),Is.Null,"The caller's persisted profile remains unchanged");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExecutingOldPluginParametersHydratesTheSnapshotBeforeThePluginReadsIt(bool pluginLayer)
        {
            var plugin=new DefaultHydrationPlugin();
            var old=new TexturePaintPluginParameterSet();old.Get("amount",true).number=0;
            if(pluginLayer)
            {
                var layer=set.AddPluginLayer("Hydration test");layer.pluginId=plugin.Descriptor.id;
                await host.ExecutePluginLayerAsync(plugin,store,old,
                    new Dictionary<TextureSet,TexturePaintLayer>{{set,layer}},null,CancellationToken.None);
            }
            else await host.ExecuteCommandAsync(plugin,store,old,null,CancellationToken.None);
            Assert.That(plugin.received.Get("depth"),Is.Not.Null);
            Assert.That(plugin.received.Float("depth"),Is.EqualTo(1));
            Assert.That(plugin.received.Float("amount"),Is.Zero);
            Assert.That(plugin.received.Boolean("enabled"),Is.True);
            Assert.That(plugin.received.Color("tint",Color.white),Is.EqualTo(Color.cyan));
            Assert.That(old.Get("depth"),Is.Null,"Execution only fills defaults in its immutable snapshot");
            if(pluginLayer)
                Assert.That(set.layers[0].pluginParameters.Float("depth"),Is.EqualTo(1),
                    "The generated layer records the complete parameters that produced its cached output");
        }
    }
}
#endif
