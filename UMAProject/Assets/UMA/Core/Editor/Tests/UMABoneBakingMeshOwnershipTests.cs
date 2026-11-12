#if UNITY_EDITOR

using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors.Tests
{
    // Bone baking writes into the renderer's mesh and keeps it. A mesh it did not create may have been marked
    // dynamic, which is slow to render with GPU skinning and cannot be detected or undone, so the combiner
    // replaces such a mesh once and then keeps writing into its own.
    public sealed class UMABoneBakingMeshOwnershipTests
    {
        private static readonly MethodInfo ensureOwnMesh = typeof(UMADefaultBoneBakingMeshCombiner).GetMethod(
            "EnsureOwnMesh", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        private GameObject gameObject;
        private SkinnedMeshRenderer renderer;

        private static Mesh Ensure(SkinnedMeshRenderer renderer, bool force32bit) =>
            (Mesh)ensureOwnMesh.Invoke(null, new object[] { renderer, force32bit });

        [SetUp]
        public void SetUp()
        {
            Assert.That(ensureOwnMesh, Is.Not.Null, "UMADefaultBoneBakingMeshCombiner.EnsureOwnMesh was not found.");
            gameObject = new GameObject("Bone baking mesh ownership test");
            renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (renderer != null && renderer.sharedMesh != null) Object.DestroyImmediate(renderer.sharedMesh);
            if (gameObject != null) Object.DestroyImmediate(gameObject);
        }

        [TestCase(IndexFormat.UInt16)]
        [TestCase(IndexFormat.UInt32)]
        public void AMeshFromElsewhereIsReplacedAndDestroyed(IndexFormat format)
        {
            // What renderer creation leaves behind when Mark Dynamic is on.
            var foreign = new Mesh { name = "UMAMesh 3", indexFormat = format };
            foreign.MarkDynamic();
            renderer.sharedMesh = foreign;

            // The other flag is deliberately the opposite: an existing mesh decides the index format.
            Mesh result = Ensure(renderer, format != IndexFormat.UInt32);

            Assert.That(result, Is.Not.SameAs(foreign));
            Assert.That(renderer.sharedMesh, Is.SameAs(result));
            Assert.That(result.name, Is.EqualTo("UMAMesh 3"));
            Assert.That(result.indexFormat, Is.EqualTo(format));
            Assert.That(result.vertexCount, Is.EqualTo(0));
            Assert.IsTrue(foreign == null, "The replaced mesh must be destroyed, not leaked.");
        }

        [Test]
        public void ItsOwnMeshIsKeptOnLaterBuilds()
        {
            renderer.sharedMesh = new Mesh { name = "UMAMesh" };
            Mesh first = Ensure(renderer, true);

            // A build fills the mesh in place.
            first.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
            first.triangles = new[] { 0, 1, 2 };

            Mesh second = Ensure(renderer, true);
            Mesh third = Ensure(renderer, false);

            Assert.That(second, Is.SameAs(first));
            Assert.That(third, Is.SameAs(first));
            Assert.That(renderer.sharedMesh, Is.SameAs(first));
            Assert.That(first.vertexCount, Is.EqualTo(3), "A mesh the combiner owns must not be cleared or replaced.");
        }

        [TestCase(true, IndexFormat.UInt32)]
        [TestCase(false, IndexFormat.UInt16)]
        public void ARendererWithoutAMeshGetsOne(bool force32bit, IndexFormat expected)
        {
            renderer.sharedMesh = null;

            Mesh result = Ensure(renderer, force32bit);

            Assert.That(result, Is.Not.Null);
            Assert.That(renderer.sharedMesh, Is.SameAs(result));
            Assert.That(result.name, Is.EqualTo("UMAMesh"));
            Assert.That(result.indexFormat, Is.EqualTo(expected));
            Assert.That(Ensure(renderer, force32bit), Is.SameAs(result));
        }

        [Test]
        public void AMeshOwnedByOneRendererIsNotTrustedBlindlyOnAnother()
        {
            // The mark belongs to the mesh, not the renderer: a mesh this combiner created stays trusted wherever
            // it is assigned, and a different mesh on the same renderer is still replaced.
            renderer.sharedMesh = new Mesh();
            Mesh own = Ensure(renderer, true);

            var other = new GameObject("Second renderer");
            try
            {
                var otherRenderer = other.AddComponent<SkinnedMeshRenderer>();
                otherRenderer.sharedMesh = own;
                Assert.That(Ensure(otherRenderer, true), Is.SameAs(own));

                var foreign = new Mesh();
                renderer.sharedMesh = foreign;
                Mesh replaced = Ensure(renderer, true);
                Assert.That(replaced, Is.Not.SameAs(foreign));
                Assert.That(replaced, Is.Not.SameAs(own));
                Assert.IsTrue(own != null, "Replacing one renderer's mesh must not destroy another mesh.");
            }
            finally
            {
                Object.DestroyImmediate(other);
                if (own != null) Object.DestroyImmediate(own);
            }
        }
    }
}
#endif
