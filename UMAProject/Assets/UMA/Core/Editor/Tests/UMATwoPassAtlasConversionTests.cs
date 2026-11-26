#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace UMA.Tests
{
    public class UMATwoPassAtlasConversionTests
    {
        private const string AtlasProperty = "_BaseMap";

        [UnityTest]
        public IEnumerator PrivateConversionRebindsBothPasses()
        {
            return VerifyConversion(reuseTextures: false);
        }

        [UnityTest]
        public IEnumerator CachedConversionRebindsBothPasses()
        {
            return VerifyConversion(reuseTextures: true);
        }

        private static IEnumerator VerifyConversion(bool reuseTextures)
        {
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Assert.Ignore("GPU readback unavailable on this test device.");
            }

            int cacheEntries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var fixture = new UMAResourceReuseAtlasTests.Fixture(convert: true, async: true))
            {
                UMAMaterial definition = fixture.Meshes.Material;
                var serialized = new SerializedObject(definition);
                serialized.FindProperty("_secondPass").objectReferenceValue =
                    fixture.Meshes.Keep(new Material(definition.material));
                serialized.ApplyModifiedPropertiesWithoutUndo();

                UMAData avatar = fixture.Avatar(reuseTextures);
                fixture.Build(avatar);
                fixture.Meshes.BuildJobified(avatar);

                UMAData.GeneratedMaterial generated = avatar.generatedMaterials.materials[0];
                var source = generated.resultingAtlasList[0] as RenderTexture;
                Assert.IsNotNull(source);
                Assert.AreSame(source, generated.material.GetTexture(AtlasProperty));
                Assert.AreSame(source, generated.secondPassMaterial.GetTexture(AtlasProperty));

                AsyncGPUReadback.WaitAllRequests();
                yield return null;
                RenderTexToCPU.ApplyQueuedCopies(0);

                Texture result = generated.resultingAtlasList[0];
                Assert.IsInstanceOf<Texture2D>(result);
                Assert.AreSame(result, generated.material.GetTexture(AtlasProperty));
                Assert.AreSame(result, generated.secondPassMaterial.GetTexture(AtlasProperty));
                Assert.IsTrue(UMARenderTextureTracker.IsReturnedToPool(source));
            }
            Assert.AreEqual(cacheEntries, UMAGeneratedResourceCache.Shared.EntryCount);
        }

        [Test]
        public void SupersededCopyLeavesNewAtlasAndClearsReleasedSourceBindings()
        {
            using (var fixture = new UMAResourceReuseAtlasTests.Fixture())
            {
                UMAData avatar = fixture.Avatar(false);
                var source = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32);
                UMARenderTextureTracker.Track(source, avatar, 0, 0, nameof(SupersededCopyLeavesNewAtlasAndClearsReleasedSourceBindings),
                    AtlasProperty, true);
                var generated = new UMAData.GeneratedMaterial
                {
                    material = new Material(fixture.Meshes.Material.material),
                    secondPassMaterial = new Material(fixture.Meshes.Material.material),
                    resultingAtlasList = new Texture[] { source }
                };
                avatar.generatedMaterials.materials.Add(generated);
                generated.material.SetTexture(AtlasProperty, source);
                generated.secondPassMaterial.SetTexture(AtlasProperty, source);

                var staleResult = new Texture2D(16, 16);
                var copy = new RenderTexToCPU(source, generated, AtlasProperty, 0, fixture.Generator)
                {
                    newTexture = staleResult
                };
                RenderTexToCPU.QueuedCopies.Enqueue(copy);

                var current = new Texture2D(16, 16);
                generated.resultingAtlasList = new Texture[] { current };
                generated.material.SetTexture(AtlasProperty, current);

                RenderTexToCPU.ApplyQueuedCopies(0);

                Assert.AreSame(current, generated.resultingAtlasList[0]);
                Assert.AreSame(current, generated.material.GetTexture(AtlasProperty));
                Assert.IsNull(generated.secondPassMaterial.GetTexture(AtlasProperty));
                Assert.IsTrue(staleResult == null);
                Assert.IsTrue(UMARenderTextureTracker.IsReturnedToPool(source));
            }
        }
    }
}
#endif
