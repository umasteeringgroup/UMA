#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    // A slot's cloth coefficients decide whether its renderer gets a Cloth component. The renderer
    // asset's UMAClothProperties only tune that cloth, and every combiner path must agree on this.
    public class UMAClothCombinerTests
    {
        private static void Build(UMAResourceReuseMeshTests.Fixture f, UMAData data, string combiner)
        {
            if (combiner == "Jobified") f.BuildJobified(data);
            else if (combiner == "Incremental") f.Build(data);
            else data.gameObject.AddComponent<UMADefaultMeshCombiner>().UpdateUMAMesh(true, data, 256);
        }

        private static void WithMeshAPI(bool useMeshAPI, System.Action action)
        {
            var settings = UMASettings.GetSettingsFromResources();
            Assert.NotNull(settings, "UMASettings must exist to choose the default combiner's path.");
            bool previous = settings.useMeshAPICombiner;
            settings.useMeshAPICombiner = useMeshAPI;
            try { action(); }
            finally { settings.useMeshAPICombiner = previous; }
        }

        private static void GiveRendererAsset(UMAResourceReuseMeshTests.Fixture f, UMAData data, float damping)
        {
            var properties = f.Keep(ScriptableObject.CreateInstance<UMAClothProperties>());
            properties.damping = damping;
            var asset = f.Keep(ScriptableObject.CreateInstance<UMARendererAsset>());
            var so = new UnityEditor.SerializedObject(asset);
            so.FindProperty("_ClothProperties").objectReferenceValue = properties;
            so.ApplyModifiedPropertiesWithoutUndo();
            data.generatedMaterials.rendererAssets[0] = asset;
            data.generatedMaterials.materials[0].rendererAsset = asset;
        }

        [TestCase("DefaultLegacy", false)] [TestCase("DefaultLegacy", true)]
        [TestCase("DefaultMeshAPI", false)] [TestCase("DefaultMeshAPI", true)]
        [TestCase("Jobified", false)] [TestCase("Jobified", true)]
        [TestCase("Incremental", false)] [TestCase("Incremental", true)]
        public void SlotCoefficientsMakeClothAndRendererAssetOnlyTunesIt(string combiner, bool withRendererAsset)
        {
            WithMeshAPI(combiner == "DefaultMeshAPI", () =>
            {
                using (var f = new UMAResourceReuseMeshTests.Fixture())
                {
                    f.Slot.meshData.clothSkinningSerialized = Enumerable.Repeat(new Vector2(0.25f, 0.75f), 3).ToArray();
                    var data = f.Avatar(false);
                    if (withRendererAsset) GiveRendererAsset(f, data, 0.37f);
                    float defaultDamping = 0f;
                    {
                        var probe = new GameObject("Default cloth probe");
                        probe.AddComponent<SkinnedMeshRenderer>();
                        defaultDamping = probe.AddComponent<Cloth>().damping;
                        Object.DestroyImmediate(probe);
                    }

                    Build(f, data, combiner);

                    var cloth = data.GetRenderer(0).GetComponent<Cloth>();
                    Assert.NotNull(cloth, "Slot cloth coefficients must give the renderer a Cloth component.");
                    Assert.AreEqual(3, cloth.coefficients.Length);
                    foreach (var coefficient in cloth.coefficients)
                    {
                        Assert.AreEqual(0.75f, coefficient.maxDistance, 1e-6f);
                        Assert.AreEqual(0.25f, coefficient.collisionSphereDistance, 1e-6f);
                    }
                    Assert.AreEqual(withRendererAsset ? 0.37f : defaultDamping, cloth.damping, 1e-6f,
                        withRendererAsset ? "The renderer asset's cloth properties must be applied." : "Without a renderer asset, Unity's default cloth settings stay.");
                }
            });
        }

        [TestCase("DefaultLegacy")] [TestCase("DefaultMeshAPI")] [TestCase("Jobified")] [TestCase("Incremental")]
        public void ClothPropertiesWithoutSlotCoefficientsAddNoCloth(string combiner)
        {
            WithMeshAPI(combiner == "DefaultMeshAPI", () =>
            {
                using (var f = new UMAResourceReuseMeshTests.Fixture())
                {
                    f.Slot.meshData.clothSkinningSerialized = null;
                    var data = f.Avatar(false);
                    GiveRendererAsset(f, data, 0.37f);

                    Build(f, data, combiner);

                    Assert.IsNull(data.GetRenderer(0).GetComponent<Cloth>(), "Cloth properties alone must not turn a renderer into cloth.");
                }
            });
        }
    }
}
#endif
