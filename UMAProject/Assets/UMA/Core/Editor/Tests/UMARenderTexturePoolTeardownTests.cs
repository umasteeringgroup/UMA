#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// Temporary atlas render textures belong to Unity's pool once UMA hands them back.
    /// UMAData teardown must never destroy them: at play-mode exit Unity refuses with
    /// "Destroying object ... is not allowed at this time", once per texture.
    /// </summary>
    public class UMARenderTexturePoolTeardownTests
    {
        private static RenderTexture TrackedTexture(UMAData data, bool temporary)
        {
            RenderTexture texture = temporary
                ? RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32)
                : new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            UMARenderTextureTracker.Track(texture, data, 0, 0, "Pool teardown test", "_BaseMap", temporary);
            return texture;
        }

        private static UMAData.GeneratedMaterial AddAtlas(UMAData data, Texture texture)
        {
            var generated = new UMAData.GeneratedMaterial
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")),
                resultingAtlasList = new Texture[] { texture }
            };
            data.generatedMaterials.materials.Add(generated);
            return generated;
        }

        [Test]
        public void PendingCopyCancelledAtPlayModeExitIsNotDestroyedByTeardown()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, true))
            {
                UMAData data = f.Avatar(false);
                RenderTexture texture = TrackedTexture(data, true);
                UMAData.GeneratedMaterial generated = AddAtlas(data, texture);
                new RenderTexToCPU(texture, generated, "_BaseMap", 0, f.Generator);

                // What UMAAssetIndexer does on ExitingPlayMode, before scene objects are destroyed.
                RenderTexToCPU.CleanupPendingCopies();

                Assert.IsTrue(ReferenceEquals(generated.resultingAtlasList[0], null),
                    "The cancelled copy must clear the atlas entry it handed back to the pool.");
                Assert.IsTrue(UMARenderTextureTracker.IsReturnedToPool(texture));

                data.CleanTextures();
                Assert.IsTrue(texture != null, "Teardown destroyed a texture owned by Unity's temporary pool.");
            }
        }

        [Test]
        public void TextureAlreadyReturnedToPoolIsLeftToUnity()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, true))
            {
                UMAData data = f.Avatar(false);
                RenderTexture texture = TrackedTexture(data, true);
                UMARenderTextureTracker.ReleaseTemporary(texture);
                // A stale atlas reference, as any other release path could leave behind.
                AddAtlas(data, texture);

                data.CleanTextures();
                Assert.IsTrue(texture != null, "Teardown destroyed a texture owned by Unity's temporary pool.");
            }
        }

        [Test]
        public void HeldTemporaryTextureGoesBackToThePool()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, true))
            {
                UMAData data = f.Avatar(false);
                RenderTexture texture = TrackedTexture(data, true);
                AddAtlas(data, texture);

                data.CleanTextures();
                Assert.IsTrue(texture != null, "A temporary render texture must be released, not destroyed.");
                Assert.IsTrue(UMARenderTextureTracker.IsReturnedToPool(texture));
            }
        }

        [Test]
        public void PrivatePersistentTextureIsStillDestroyed()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, true))
            {
                UMAData data = f.Avatar(false);
                RenderTexture texture = TrackedTexture(data, false);
                AddAtlas(data, texture);

                data.CleanTextures();
                Assert.IsTrue(texture == null, "A private persistent atlas texture must still be destroyed.");
            }
        }
    }
}
#endif
