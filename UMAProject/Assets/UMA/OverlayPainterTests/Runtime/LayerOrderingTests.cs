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

        [Test]
        public void NestedGroupsNormalizeToContiguousPostOrder()
        {
            using TextureSet set = new TextureSet();
            TexturePaintLayer outer = set.AddGroup("Outer");
            TexturePaintLayer inner = set.AddGroup("Inner");
            TexturePaintLayer leaf = set.AddLayer("Leaf");
            set.activeLayerIndex = -1;
            TexturePaintLayer root = set.AddLayer("Root");

            set.layers.Remove(root);
            set.layers.Insert(1, root);
            Assert.That(set.NormalizeLayerHierarchy(), Is.True);

            Assert.That(inner.parentId, Is.EqualTo(outer.id));
            Assert.That(leaf.parentId, Is.EqualTo(inner.id));
            Assert.That(set.layers, Is.EqualTo(new[] { leaf, inner, outer, root }));
        }

        [Test]
        public void RemovingGroupCascadesThroughEveryDescendant()
        {
            using TextureSet set = new TextureSet();
            TexturePaintLayer outer = set.AddGroup("Outer");
            TexturePaintLayer inner = set.AddGroup("Inner");
            set.AddLayer("Leaf");
            set.activeLayerIndex = -1;
            TexturePaintLayer survivor = set.AddLayer("Survivor");

            Assert.That(set.RemoveLayerAt(set.layers.IndexOf(outer)), Is.True);

            Assert.That(set.layers, Is.EqualTo(new[] { survivor }));
            Assert.That(set.activeLayerIndex, Is.EqualTo(0));
            Assert.That(inner.parentId, Is.EqualTo(outer.id));
        }

        [Test]
        public void DuplicatingGroupDeepCopiesParentsAndClearsProceduralOwnership()
        {
            using TextureSet set = new TextureSet();
            TexturePaintLayer outer = set.AddGroup("Outer");
            outer.proceduralGroupKey = "generated-original";
            TexturePaintLayer inner = set.AddGroup("Inner");
            TexturePaintLayer leaf = set.AddLayer("Leaf");
            leaf.proceduralGroupKey = "generated-original";

            TexturePaintLayer copy = set.DuplicateLayerAt(set.layers.IndexOf(outer));

            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.kind, Is.EqualTo(TexturePaintLayerKind.Group));
            Assert.That(copy.id, Is.Not.EqualTo(outer.id));
            Assert.That(copy.proceduralGroupKey, Is.Null);
            TexturePaintLayer innerCopy = set.layers.Find(candidate =>
                candidate != null && candidate.kind == TexturePaintLayerKind.Group &&
                candidate != inner && candidate.parentId == copy.id);
            Assert.That(innerCopy, Is.Not.Null);
            TexturePaintLayer leafCopy = set.layers.Find(candidate =>
                candidate != null && candidate != leaf && candidate.parentId == innerCopy.id);
            Assert.That(leafCopy, Is.Not.Null);
            Assert.That(leafCopy.proceduralGroupKey, Is.Null);
            Assert.That(leaf.parentId, Is.EqualTo(inner.id));
        }

        [Test]
        public void MovingGroupMovesItsEntireNestedSubtreeAsOneBlock()
        {
            using TextureSet set = new TextureSet();
            TexturePaintLayer outer = set.AddGroup("Outer");
            TexturePaintLayer inner = set.AddGroup("Inner");
            TexturePaintLayer leaf = set.AddLayer("Leaf");
            set.activeLayerIndex = -1;
            TexturePaintLayer root = set.AddLayer("Root");
            set.activeLayerIndex = set.layers.IndexOf(leaf);

            Assert.That(set.MoveLayer(set.layers.IndexOf(outer), set.layers.IndexOf(root)), Is.True);

            Assert.That(set.layers, Is.EqualTo(new[] { root, leaf, inner, outer }));
            Assert.That(leaf.parentId, Is.EqualTo(inner.id));
            Assert.That(inner.parentId, Is.EqualTo(outer.id));
            Assert.That(set.layers[set.activeLayerIndex], Is.SameAs(leaf));
        }
    }
}
#endif
