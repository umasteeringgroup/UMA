#if UNITY_EDITOR
using System;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    public class UMAGeneratedResourceCacheTests
    {
        private static UMAGeneratedResourceKey Key(string text = "same") => new UMAGeneratedResourceKey("mesh-v1", Encoding.UTF8.GetBytes(text));

        [Test]
        public void ExactKeysCopyTheirDescriptionAndSeparateKindsAndInputs()
        {
            var bytes = new byte[] { 1, 2, 3 };
            var key = new UMAGeneratedResourceKey("mesh", bytes);
            bytes[0] = 9;
            Assert.AreEqual(key, new UMAGeneratedResourceKey("mesh", new byte[] { 1, 2, 3 }));
            Assert.AreNotEqual(key, new UMAGeneratedResourceKey("texture", new byte[] { 1, 2, 3 }));
            Assert.AreNotEqual(key, new UMAGeneratedResourceKey("mesh", bytes));
        }

        [Test]
        public void HundredOwnersShareOneOutputAndDestroyItExactlyOnceAtLastRelease()
        {
            var cache = new UMAGeneratedResourceCache();
            var owners = new UMAGeneratedResourceCache.Lease<Mesh>[100];
            int destroyed = 0;
            var mesh = new Mesh();
            try
            {
                owners[0] = cache.Acquire<Mesh>(Key());
                owners[0].Publish(mesh, "bone mapping", value => { destroyed++; UnityEngine.Object.DestroyImmediate(value); });
                for (int i = 1; i < owners.Length; i++)
                {
                    owners[i] = cache.Acquire<Mesh>(Key());
                    Assert.AreSame(mesh, owners[i].Resource);
                    Assert.AreEqual("bone mapping", owners[i].Metadata);
                }
                Assert.AreEqual(1, cache.EntryCount);
                Assert.AreEqual(100, cache.ReferenceCount);
                for (int i = 0; i < 99; i++) { owners[i].Dispose(); owners[i].Dispose(); }
                Assert.AreEqual(0, destroyed);
                Assert.IsTrue(owners[99].Resource != null);
                owners[99].Dispose();
                Assert.AreEqual(1, destroyed);
                Assert.AreEqual(0, cache.EntryCount);
                Assert.AreEqual(0, cache.ReferenceCount);
                Assert.AreEqual(99, cache.Hits);
            }
            finally
            {
                foreach (var owner in owners) owner?.Dispose();
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void InvalidatingAnEntryKeepsExistingOwnersAliveAndTheirReleaseDoesNotRemoveTheReplacement()
        {
            var cache = new UMAGeneratedResourceCache();
            using (var old = cache.Acquire<Mesh>(Key()))
            {
                old.Publish(new Mesh());
                cache.Invalidate(Key());
                using (var replacement = cache.Acquire<Mesh>(Key()))
                {
                    Assert.IsTrue(replacement.IsBuilder); Assert.IsTrue(old.Resource != null);
                    replacement.Publish(new Mesh());
                    old.Dispose();
                    Assert.AreEqual(1, cache.EntryCount);
                    using (var follower = cache.Acquire<Mesh>(Key())) Assert.AreSame(replacement.Resource, follower.Resource);
                }
            }
            Assert.AreEqual(0, cache.EntryCount); Assert.AreEqual(0, cache.ReferenceCount);
        }

        [Test]
        public void CancelledProducerPromotesFollowerWithoutStrandingOtherRequests()
        {
            var cache = new UMAGeneratedResourceCache();
            using (var first = cache.Acquire<Mesh>(Key()))
            using (var next = cache.Acquire<Mesh>(Key()))
            using (var last = cache.Acquire<Mesh>(Key()))
            {
                Assert.IsTrue(first.IsBuilder);
                Assert.IsFalse(next.IsBuilder);
                Assert.IsFalse(next.IsReady);
                first.Dispose();
                Assert.IsTrue(next.IsBuilder);
                next.Publish(new Mesh());
                Assert.IsTrue(last.IsReady);
                Assert.AreSame(next.Resource, last.Resource);
                Assert.AreEqual(2, cache.PendingJoins);
            }
            Assert.AreEqual(0, cache.EntryCount);
        }

        [Test]
        public void AbandonedBuildLeavesNoEntryAndCanBeRetried()
        {
            var cache = new UMAGeneratedResourceCache();
            cache.Acquire<Mesh>(Key()).Dispose();
            Assert.AreEqual(0, cache.EntryCount);
            using (var retry = cache.Acquire<Mesh>(Key())) Assert.IsTrue(retry.IsBuilder);
            Assert.AreEqual(0, cache.ReferenceCount);
        }

        [Test]
        public void OnlyBuilderCanPublishAndReleasedHandlesCannotPublish()
        {
            var cache = new UMAGeneratedResourceCache();
            var mesh = new Mesh();
            try
            {
                using (var first = cache.Acquire<Mesh>(Key()))
                using (var follower = cache.Acquire<Mesh>(Key()))
                {
                    Assert.Throws<InvalidOperationException>(() => follower.Publish(mesh));
                    first.Dispose();
                    Assert.Throws<ObjectDisposedException>(() => first.Publish(mesh));
                    follower.Publish(mesh);
                    Assert.Throws<InvalidOperationException>(() => follower.Publish(mesh));
                }
                Assert.IsTrue(mesh == null);
            }
            finally { if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void RebuildAcquiresBeforeReleasingOldHandleWithoutDestroyingSharedOutput()
        {
            var cache = new UMAGeneratedResourceCache();
            using (var old = cache.Acquire<Mesh>(Key()))
            {
                old.Publish(new Mesh());
                using (var replacement = cache.Acquire<Mesh>(Key()))
                {
                    old.Dispose();
                    Assert.IsTrue(replacement.Resource != null);
                }
            }
            Assert.AreEqual(0, cache.ReferenceCount);
        }
    }
}
#endif
