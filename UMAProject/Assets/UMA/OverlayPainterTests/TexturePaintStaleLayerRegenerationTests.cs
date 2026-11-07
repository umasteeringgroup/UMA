#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintStaleLayerRegenerationTests
    {
        private static TexturePaintLayer Plugin(string name, bool stale = true) =>
            new TexturePaintLayer { name = name, kind = TexturePaintLayerKind.Plugin,
                pluginId = "test.generator", pluginStale = stale, visible = false };

        [Test]
        public async Task BatchReadsLiveBottomToTopStateAndIncludesHiddenLayersAndMasks()
        {
            var first = new TextureSet(); var second = new TextureSet();
            var lower = Plugin("lower"); var upper = Plugin("upper", false);
            var peer = Plugin("peer"); var other = Plugin("other");
            var masked = new TexturePaintLayer { name = "mask", kind = TexturePaintLayerKind.Paint,
                layerMask = new TexturePaintLayerMask { pluginId = "test.mask", pluginStale = true } };
            first.layers.AddRange(new[] { lower, masked, upper });
            second.layers.AddRange(new[] { peer, other });
            var sets = new[] { first, second };
            var order = new List<string>();
            int count = await TexturePaintStageWindow.RegenerateStaleGeneratorsAsync(sets, (set, layer, mask) =>
            {
                order.Add(layer.name);
                if (mask) layer.layerMask.pluginStale = false;
                else
                {
                    var replacement = Plugin(layer.name, false);
                    replacement.id = layer.id;
                    set.layers[set.layers.IndexOf(layer)] = replacement;
                    if (layer == lower)
                    {
                        // Actual commits replace objects, invalidate dependents and generate
                        // all logical peers; none of these should leave a stale queued reference.
                        upper.pluginStale = true;
                        peer.pluginStale = false;
                    }
                }
                return Task.FromResult(true);
            }, () => true);
            Assert.That(order, Is.EqualTo(new[] { "lower", "mask", "upper", "other" }));
            Assert.That(count, Is.EqualTo(4));
            Assert.That(TexturePaintStageWindow.FindStaleGenerator(sets, out _, out _, out _), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BatchStopsOnFailureOrCancellation(bool cancel)
        {
            var set = new TextureSet(); var lower = Plugin("lower"); var upper = Plugin("upper");
            set.layers.AddRange(new[] { lower, upper });
            bool keepGoing = true; int attempts = 0;
            int count = await TexturePaintStageWindow.RegenerateStaleGeneratorsAsync(new[] { set }, (s, layer, mask) =>
            {
                attempts++;
                if (cancel) { layer.pluginStale = false; keepGoing = false; }
                return Task.FromResult(cancel);
            }, () => keepGoing);
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(count, Is.EqualTo(cancel ? 1 : 0));
            Assert.That(upper.pluginStale, Is.True);
        }

        [Test]
        public async Task NoOpCannotLoopForeverAndEmptyStackHasNoWork()
        {
            var set = new TextureSet();
            Assert.That(TexturePaintStageWindow.FindStaleGenerator(new[] { set }, out _, out _, out _), Is.False);
            set.layers.Add(Plugin("unchanged"));
            int attempts = 0;
            await TexturePaintStageWindow.RegenerateStaleGeneratorsAsync(new[] { set }, (s, layer, mask) =>
            { attempts++; return Task.FromResult(true); }, () => true);
            Assert.That(attempts, Is.EqualTo(1));
        }
    }
}
#endif
