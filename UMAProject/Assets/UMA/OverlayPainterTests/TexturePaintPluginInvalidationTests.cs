#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPluginInvalidationTests
    {
        private static TexturePaintLayer Plugin(TextureSet set,string name)
        {
            var layer=new TexturePaintLayer{name=name,kind=TexturePaintLayerKind.Plugin,
                pluginStale=false,pluginLastError=name+" diagnostic"};
            set.layers.Add(layer);return layer;
        }
        private static void Invalidate(TextureSet set,TexturePaintLayer changed)
            =>typeof(TexturePaintStageWindow).GetMethod("MarkPluginLayersAffectedByLayer",
                BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{set,changed});

        [Test]
        public void SuccessfulPluginReplacementInvalidatesOnlyPluginsAboveItsRetainedIdentity()
        {
            using var set=new TextureSet();
            var lower=Plugin(set,"Lower");var previous=Plugin(set,"Generated");var upper=Plugin(set,"Upper");
            var replacement=new TexturePaintLayer{id=previous.id,name="Generated",kind=TexturePaintLayerKind.Plugin,
                pluginStale=false,pluginLastError=null};
            set.layers[1]=replacement;
            try
            {
                Invalidate(set,previous);
                Assert.That(replacement.pluginStale,Is.False,"The newly committed generator must remain up to date");
                Assert.That(replacement.pluginLastError,Is.Null);
                Assert.That(lower.pluginStale,Is.False,"Lower generators do not depend on the changed output");
                Assert.That(lower.pluginLastError,Is.EqualTo("Lower diagnostic"));
                Assert.That(upper.pluginStale,Is.True);
                Assert.That(upper.pluginLastError,Is.Null);
            }
            finally{previous.Dispose();}
        }

        [TestCase("missing")] [TestCase("")] [TestCase(null)]
        public void UnresolvedOrDeletedLayerIdentityConservativelyInvalidatesAllPlugins(string id)
        {
            using var set=new TextureSet();var lower=Plugin(set,"Lower");var upper=Plugin(set,"Upper");
            using var removed=id==null ? null : new TexturePaintLayer{id=id,kind=TexturePaintLayerKind.Paint};
            Invalidate(set,removed);
            Assert.That(lower.pluginStale,Is.True);Assert.That(upper.pluginStale,Is.True);
            Assert.That(lower.pluginLastError,Is.Null);Assert.That(upper.pluginLastError,Is.Null);
        }

        [Test]
        public void RetainedIdentityResolutionUsesTheLiveGroupsDescendantDependencies()
        {
            using var set=new TextureSet();var unrelated=Plugin(set,"Unrelated");var child=Plugin(set,"Child");
            using var previous=new TexturePaintLayer{kind=TexturePaintLayerKind.Group};
            var group=new TexturePaintLayer{id=previous.id,kind=TexturePaintLayerKind.Group};
            child.parentId=group.id;set.layers.Add(group);var upper=Plugin(set,"Upper");
            Invalidate(set,previous);
            Assert.That(unrelated.pluginStale,Is.False);
            Assert.That(child.pluginStale,Is.True,"A group's changed mask/opacity affects its descendant generators");
            Assert.That(upper.pluginStale,Is.True);
        }
    }
}
#endif
