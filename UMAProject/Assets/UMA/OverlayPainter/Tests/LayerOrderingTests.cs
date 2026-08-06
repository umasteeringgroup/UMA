#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace UMA.TexturePaint.Tests
{
    public sealed class LayerOrderingTests
    {
        [Test]
        public void MoveLayerPreservesActiveLayerIdentity()
        {
            TextureSet set = new TextureSet();
            TexturePaintLayer first = set.AddLayer("First");
            TexturePaintLayer active = set.AddLayer("Active");
            TexturePaintLayer last = set.AddLayer("Last");
            set.activeLayerIndex = 1;

            Assert.That(set.MoveLayer(0, 2), Is.True);
            Assert.That(set.layers, Is.EqualTo(new[] { active, last, first }));
            Assert.That(set.activeLayerIndex, Is.EqualTo(0));
        }

        [Test]
        public void RemoveLayerSelectsNearestRemainingLayer()
        {
            TextureSet set = new TextureSet();
            set.AddLayer("First");
            set.AddLayer("Second");
            TexturePaintLayer third = set.AddLayer("Third");
            set.activeLayerIndex = 1;

            Assert.That(set.RemoveLayerAt(1), Is.True);
            Assert.That(set.layers, Has.Count.EqualTo(2));
            Assert.That(set.layers[set.activeLayerIndex], Is.SameAs(third));
        }
    }
}
#endif
