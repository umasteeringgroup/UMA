#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMAProjectDefaultsTests
    {
        private string folder;
        private GameObject prefab;
        private UMAMaterial skin, existing;

        [SetUp] public void SetUp()
        {
            string name = "UMA_Defaults_Test_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            var root = new GameObject("Generator"); root.SetActive(false);
            try
            {
                var gen = root.AddComponent<UMAGenerator>();
                gen.meshCombiner = root.AddComponent<UMADefaultMeshCombiner>();
                gen.processAllPending = true; gen.MaxMultiStepWorkMilliseconds = 40;
                gen.convertRenderTexture = true; gen.alwaysRegenerateRenderers = true;
                gen.atlasResolution = 2048; gen.editorAtlasResolution = 512;
                gen.InitialScaleFactor = 2; gen.editorInitialScaleFactor = 4;
                gen.keepTag = "CustomKeep"; gen.autoSetRaceBlendshapes = true;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Generator.prefab");
            }
            finally { Object.DestroyImmediate(root); }
            skin = ScriptableObject.CreateInstance<UMAMaterial>(); skin.name = "Custom Skin";
            skin.materialType = UMAMaterial.MaterialType.NoAtlas; skin.generateMipMaps = false;
            AssetDatabase.CreateAsset(skin, folder + "/Skin.asset");
            existing = ScriptableObject.CreateInstance<UMAMaterial>(); existing.name = "Existing Skin";
            existing.materialType = UMAMaterial.MaterialType.UseExistingMaterial;
            AssetDatabase.CreateAsset(existing, folder + "/Existing.asset");
        }

        [TearDown] public void TearDown()
        {
            if (!string.IsNullOrEmpty(folder)) AssetDatabase.DeleteAsset(folder);
        }

        [Test] public void ModesConfigureSavedPrefabAndMaterialsWithoutDiscardingCustomSettings()
        {
            foreach (bool speed in new[] { true, false, true })
            {
                UMAProjectDefaults.Apply(prefab, new[] { skin, existing, skin }, speed);
                var loaded = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
                try
                {
                    var gen = loaded.GetComponent<UMAGenerator>();
                    Assert.That(gen.meshCombiner, Is.TypeOf<UMAJobifiedMeshCombiner>());
                    Assert.That(gen.meshCombiner.gameObject, Is.SameAs(gen.gameObject));
                    Assert.That(gen.meshCombiner.enabled, Is.True);
                    Assert.That(loaded.GetComponents<UMAJobifiedMeshCombiner>().Length, Is.EqualTo(1));
                    Assert.That(loaded.GetComponent<UMADefaultMeshCombiner>(), Is.Not.Null);
                    Assert.That(gen.processAllPending, Is.False); Assert.That(gen.IterationCount, Is.EqualTo(1));
                    Assert.That(gen.MaxMultiStepWorkMilliseconds, Is.EqualTo(2));
                    Assert.That(gen.garbageCollectionRate, Is.EqualTo(128));
                    Assert.That(gen.convertRenderTexture, Is.False); Assert.That(gen.useAsyncConversion, Is.True);
                    Assert.That(gen.alwaysRegenerateRenderers, Is.False);
                    Assert.That(gen.CleanupUnusedBones, Is.EqualTo(speed));
                    Assert.That(gen.enableSourceUVCropping, Is.EqualTo(speed));
                    Assert.That(gen.SaveAndRestoreIgnoredItems, Is.True);
                    Assert.That(gen.atlasResolution, Is.EqualTo(2048)); Assert.That(gen.editorAtlasResolution, Is.EqualTo(512));
                    Assert.That(gen.InitialScaleFactor, Is.EqualTo(2)); Assert.That(gen.editorInitialScaleFactor, Is.EqualTo(4));
                    Assert.That(gen.keepTag, Is.EqualTo("CustomKeep")); Assert.That(gen.autoSetRaceBlendshapes, Is.True);
                }
                finally { PrefabUtility.UnloadPrefabContents(loaded); }
                Assert.That(skin.materialType, Is.EqualTo(speed ? UMAMaterial.MaterialType.Atlas : UMAMaterial.MaterialType.NoAtlas));
                Assert.That(skin.generateMipMaps, Is.False);
                Assert.That(existing.materialType, Is.EqualTo(UMAMaterial.MaterialType.UseExistingMaterial));
            }
        }

        [Test] public void ReapplyingModeDoesNotRewriteAssetsOrDuplicateCombiner()
        {
            UMAProjectDefaults.Apply(prefab, new[] { skin }, true);
            string path = AssetDatabase.GetAssetPath(prefab);
            var before = File.ReadAllBytes(path);
            var timestamp = File.GetLastWriteTimeUtc(path);
            UMAProjectDefaults.Apply(prefab, new[] { skin }, true);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(timestamp));
        }

        [Test] public void InvalidMaterialIsRejectedBeforeAnyAssetsChange()
        {
            string path = AssetDatabase.GetAssetPath(prefab); var before = File.ReadAllBytes(path);
            var transient = ScriptableObject.CreateInstance<UMAMaterial>();
            try { Assert.Throws<InvalidOperationException>(() => UMAProjectDefaults.Apply(prefab, new[] { skin, transient }, true)); }
            finally { Object.DestroyImmediate(transient); }
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            Assert.That(skin.materialType, Is.EqualTo(UMAMaterial.MaterialType.NoAtlas));
            Assert.Throws<InvalidOperationException>(() => UMAProjectDefaults.Apply(null, new[] { skin }, true));
        }

        [Test] public void SkinDiscoveryDoesNotIncludeOrdinaryClothingMaterials()
        {
            Assert.That(UMAProjectDefaults.IsSkinMaterial(skin), Is.True);
            skin.name = "Leather";
            Assert.That(UMAProjectDefaults.IsSkinMaterial(skin), Is.False);
            Assert.That(UMAProjectDefaults.CanSwitchMaterial(existing), Is.False);
            Assert.That(UMAProjectDefaults.IsSkinMaterial(null), Is.False);
        }
    }
}
#endif
