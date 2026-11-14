#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.Tests
{
    public class UMAResourceReuseFirstStageTests
    {
        private static long Counter(string field) => (long)typeof(UMAResourceReuse)
            .GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        [Test]
        public void DifferentSignaturesNeverBuildDetailedKeysButAnIdenticalRequestDoes()
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                long detailed = Counter("MeshDetailedKeyCount"), rejected = Counter("MeshFirstStageRejectCount");
                Mesh last = null;
                for (int i = 0; i < 20; i++)
                {
                    var avatar = f.Avatar();
                    avatar.blendShapeSettings.blendShapes["Smile"] = new BlendShapeData { isBaked = true, value = i / 20f };
                    f.Build(avatar);
                    last = avatar.GetRenderer(0).sharedMesh;
                }
                Assert.That(Counter("MeshDetailedKeyCount"), Is.EqualTo(detailed), "Unique requests must not defer the same expensive work to publication.");
                Assert.That(Counter("MeshFirstStageRejectCount") - rejected, Is.EqualTo(20));
                var identical = f.Avatar();
                identical.blendShapeSettings.blendShapes["Smile"] = new BlendShapeData { isBaked = true, value = 19 / 20f };
                f.Build(identical);
                Assert.That(identical.GetRenderer(0).sharedMesh, Is.SameAs(last));
                Assert.That(Counter("MeshDetailedKeyCount") - detailed, Is.EqualTo(2), "Only the candidate and the matching request need detailed keys.");
            }
        }

        [Test]
        public void SameFirstStageWithDifferentGeometryDoesNotShareAndEqualCopiesStillShare()
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var first = new UMAResourceReuseMeshTests.Fixture())
            using (var different = new UMAResourceReuseMeshTests.Fixture())
            using (var equalCopy = new UMAResourceReuseMeshTests.Fixture())
            {
                different.Slot.meshData.vertices[0] = Vector3.forward;
                var a = first.Avatar(); var b = different.Avatar(); var c = equalCopy.Avatar();
                long detailed = Counter("MeshDetailedKeyCount");
                first.Build(a); different.Build(b); equalCopy.Build(c);
                Assert.That(b.GetRenderer(0).sharedMesh, Is.Not.SameAs(a.GetRenderer(0).sharedMesh));
                Assert.That(b.GetRenderer(0).sharedMesh.vertices[0], Is.Not.EqualTo(a.GetRenderer(0).sharedMesh.vertices[0]));
                Assert.That(c.GetRenderer(0).sharedMesh, Is.SameAs(a.GetRenderer(0).sharedMesh), "Separate equal source objects must not be rejected by reference identity.");
                Assert.That(Counter("MeshDetailedKeyCount") - detailed, Is.EqualTo(3));
            }
            Assert.That(UMAGeneratedResourceCache.Shared.EntryCount, Is.EqualTo(entries));
        }

        [TestCase(false)] [TestCase(true)]
        public void PendingInputChangesNeverPublishAStaleLazyCandidate(bool invalidateGeometry)
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                var avatar = f.Avatar();
                long admitted = Counter("MeshAdmissionCount"), detailed = Counter("MeshDetailedKeyCount");
                long published = UMAGeneratedResourceCache.Shared.GetStatistics<Mesh>().Publications;
                using (var operation = avatar.GetComponent<UMAIncrementalMeshCombiner>().BeginUpdateUMAMesh(true, avatar, 256))
                {
                    for (int i = 0; i < 1000 && Counter("MeshAdmissionCount") == admitted; i++) Step(operation);
                    Assert.That(Counter("MeshAdmissionCount"), Is.GreaterThan(admitted));
                    if (invalidateGeometry)
                    {
                        f.Slot.meshData.vertices[0] = Vector3.forward;
                        UMAResourceReuse.InvalidateMeshSource(f.Slot.meshData);
                    }
                    else avatar.generatedMaterials.materials[0].materialFragments[0].atlasRegion.width = 128;
                    bool done = false;
                    for (int i = 0; i < 100000 && !done; i++) done = Step(operation);
                    Assert.That(done, Is.True);
                    Assert.That(UMAGeneratedResourceCache.Shared.GetStatistics<Mesh>().Publications, Is.EqualTo(published));
                    Assert.That(Counter("MeshDetailedKeyCount"), Is.EqualTo(detailed));
                }
            }
            Assert.That(UMAGeneratedResourceCache.Shared.EntryCount, Is.EqualTo(entries));
        }

        [UnityTest]
        public IEnumerator ReleasingAnUnresolvedCandidateReleasesItsSourceReferences()
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            int owners = UMAGeneratedResourceCache.Shared.ReferenceCount;
            var references = ReleasedReferences(out var disposedLease);
            Assert.That(disposedLease.IsReleased, Is.True);
            Assert.That(UMAGeneratedResourceCache.Shared.EntryCount, Is.EqualTo(entries));
            Assert.That(UMAGeneratedResourceCache.Shared.ReferenceCount, Is.EqualTo(owners));
            AssertReleasedEntryCleared(disposedLease);

            // Let the fixture's call stack unwind before collecting. Immediate
            // collection can retain temporary stack roots in the Editor even
            // though the disposed cache entry no longer references the source.
            // The iterator retains the disposed lease across this frame boundary.
            yield return null;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.That(references[0].IsAlive, Is.False, "The source mesh must be collectible.");
            Assert.That(references[1].IsAlive, Is.False, "The source vertices must be collectible.");
            GC.KeepAlive(disposedLease);
        }

        private static void AssertReleasedEntryCleared(UMAGeneratedResourceCache.Lease<Mesh> lease)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var entry = lease.GetType().GetField("entry", flags).GetValue(lease);
            foreach (string field in new[] { "Key", "Resource", "Metadata", "Destroy", "CompleteBuild" })
                Assert.That(entry.GetType().GetField(field, flags).GetValue(entry), Is.Null,
                    "A fully released cache entry must clear " + field + ".");
            var requests = (ICollection)entry.GetType().GetField("Requests", flags).GetValue(entry);
            Assert.That(requests.Count, Is.Zero, "The released entry must have no remaining owners.");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference[] ReleasedReferences(out UMAGeneratedResourceCache.Lease<Mesh> disposedLease)
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                var data = f.Avatar(); f.Build(data);
                disposedLease = UMAGeneratedResourceCache.RetainSharedResource(data.GetRenderer(0).sharedMesh);
                var references = new[] { new WeakReference(f.Slot.meshData), new WeakReference(f.Slot.meshData.vertices) };
                UnityEngine.Object.DestroyImmediate(data.gameObject);
                disposedLease.Dispose();
                return references;
            }
        }

        private static bool Step(IUMAMeshCombineOperation operation)
        {
            var result = operation.Step(UMAMeshCombineTimeSlice.Unlimited);
            if (result.Status == UMAMeshCombineStatus.Failed) Assert.Fail(result.Error.ToString());
            return result.Status == UMAMeshCombineStatus.Completed;
        }
    }
}
#endif
