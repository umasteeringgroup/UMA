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
                gen.forceGPUTextureCompression = true;
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
                    Assert.That(gen.forceGPUTextureCompression, Is.EqualTo(speed), "GPU compression is on for Maximum speed only.");
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
                Assert.That(skin.AnisoLevel, Is.EqualTo(speed ? UMAProjectDefaults.SpeedAnisoLevel : UMAProjectDefaults.FidelityAnisoLevel));
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

        private Material SkinMaterialOf(UMAMaterial material)
        {
            using var serialized = new SerializedObject(material);
            return serialized.FindProperty("_material").objectReferenceValue as Material;
        }

        private static string PathOf(Material material) => AssetDatabase.GetAssetPath(material);

        private static void SetSkinValue(string path, string property, float value)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            material.SetFloat(property, value);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }

        private Material AssignSkinShader(string shaderName, string assetName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) Assert.Ignore(shaderName + " is not installed.");
            var material = new Material(shader) { name = assetName };
            AssetDatabase.CreateAsset(material, folder + "/" + assetName + ".mat");
            using var serialized = new SerializedObject(skin);
            serialized.FindProperty("_material").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(skin);
            return material;
        }

        [Test] public void SpeedSwapsRealisticSkinSSSForARealisticCopyAndFidelityRestoresIt()
        {
            var sss = AssignSkinShader(UMARealisticSkinUtility.SSSShaderName, "SkinSSS");
            SetSkinValue(PathOf(sss), "_BumpScale", 1.25f);
            string copyPath = folder + "/SkinSSS" + UMAProjectDefaults.SpeedSkinSuffix + ".mat";

            UMAProjectDefaults.Apply(prefab, new[] { skin }, true);
            var copy = SkinMaterialOf(skin);
            Assert.That(AssetDatabase.GetAssetPath(copy), Is.EqualTo(copyPath));
            Assert.That(copy.shader.name, Is.EqualTo(UMARealisticSkinUtility.ShaderName));
            Assert.That(copy.GetFloat("_BumpScale"), Is.EqualTo(1.25f));
            Assert.That(UMAProjectDefaults.HasSkinShaderBackup(skin), Is.True);
            string copyGuid = AssetDatabase.AssetPathToGUID(copyPath);

            UMAProjectDefaults.Apply(prefab, new[] { skin }, true); // Repeating Speed keeps the SSS backup.
            Assert.That(PathOf(SkinMaterialOf(skin)), Is.EqualTo(copyPath));

            UMAProjectDefaults.Apply(prefab, new[] { skin }, false);
            Assert.That(PathOf(SkinMaterialOf(skin)), Is.EqualTo(PathOf(sss)));
            Assert.That(UMAProjectDefaults.HasSkinShaderBackup(skin), Is.False);

            // SSS edits made while restored reach the same copy on the next Speed run.
            SetSkinValue(PathOf(sss), "_BumpScale", 1.75f);
            UMAProjectDefaults.Apply(prefab, new[] { skin }, true);
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(SkinMaterialOf(skin))), Is.EqualTo(copyGuid));
            Assert.That(SkinMaterialOf(skin).GetFloat("_BumpScale"), Is.EqualTo(1.75f));
            Assert.That(SkinMaterialOf(skin).name, Is.EqualTo("SkinSSS" + UMAProjectDefaults.SpeedSkinSuffix));
        }

        [Test] public void SpeedKeepsTheWrinkleShaderAndOtherSkinShaders()
        {
            foreach (string shaderName in new[] { UMARealisticSkinUtility.WrinkleShaderName, UMARealisticSkinUtility.ShaderName })
            {
                var material = AssignSkinShader(shaderName, "Skin" + shaderName.GetHashCode().ToString("X"));
                UMAProjectDefaults.Apply(prefab, new[] { skin }, true);
                Assert.That(PathOf(SkinMaterialOf(skin)), Is.EqualTo(PathOf(material)), shaderName);
                Assert.That(UMAProjectDefaults.HasSkinShaderBackup(skin), Is.False, shaderName);
                UMAProjectDefaults.Apply(prefab, new[] { skin }, false);
                Assert.That(PathOf(SkinMaterialOf(skin)), Is.EqualTo(PathOf(material)), shaderName);
            }
            Assert.That(AssetDatabase.FindAssets(UMAProjectDefaults.SpeedSkinSuffix, new[] { folder }), Is.Empty);
        }

        private Material MakePass(string name, bool alphaClip)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);
            var pass = new Material(shader) { name = name, renderQueue = alphaClip ? 2450 : 3000 };
            pass.SetOverrideTag("RenderType", alphaClip ? "TransparentCutout" : "Transparent");
            if (pass.HasProperty("_AlphaClip")) pass.SetFloat("_AlphaClip", alphaClip ? 1 : 0);
            if (pass.HasProperty("_Surface")) pass.SetFloat("_Surface", alphaClip ? 0 : 1);
            if (alphaClip) pass.EnableKeyword("_ALPHATEST_ON");
            AssetDatabase.CreateAsset(pass, folder + "/" + name + ".mat");
            return pass;
        }

        private UMAMaterial MakeHair(params Material[] passes)
        {
            var hair = ScriptableObject.CreateInstance<UMAMaterial>();
            hair.name = "Custom Hair";
            hair.materialType = UMAMaterial.MaterialType.UseExistingTextures;
            hair.generateMipMaps = false;
            AssetDatabase.CreateAsset(hair, folder + "/Hair.asset");
            using var serialized = new SerializedObject(hair);
            string[] fields = { "_material", "_secondPass", "_HDRPMaterial", "_HDRPSecondPass" };
            for (int i = 0; i < passes.Length; i++) serialized.FindProperty(fields[i]).objectReferenceValue = passes[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(hair);
            return hair;
        }

        private static Material[] ReadPasses(UMAMaterial material)
        {
            using var serialized = new SerializedObject(material);
            return new[]
            {
                (Material)serialized.FindProperty("_material").objectReferenceValue,
                (Material)serialized.FindProperty("_secondPass").objectReferenceValue,
                (Material)serialized.FindProperty("_HDRPMaterial").objectReferenceValue,
                (Material)serialized.FindProperty("_HDRPSecondPass").objectReferenceValue
            };
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void HairPresetsKeepOnlyAlphaClipAndRestoreBothPipelineAssignments(bool defaultClipSecond, bool hdrpClipSecond)
        {
            var clip = MakePass("HairClip", true);
            var transparent = MakePass("HairTransparent", false);
            var hdrpClip = MakePass("HDRPHairClip", true);
            var hdrpTransparent = MakePass("HDRPHairTransparent", false);
            var original = new[]
            {
                defaultClipSecond ? transparent : clip, defaultClipSecond ? clip : transparent,
                hdrpClipSecond ? hdrpTransparent : hdrpClip, hdrpClipSecond ? hdrpClip : hdrpTransparent
            };
            var hair = MakeHair(original);
            var clipBytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(clip));
            var transparentBytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(transparent));

            UMAProjectDefaults.Apply(prefab, new[] { skin }, true, new[] { hair, hair });
            Assert.That(ReadPasses(hair), Is.EqualTo(new Material[] { clip, null, hdrpClip, null }));
            Assert.That(UMAProjectDefaults.HasHairMaterialBackup(hair), Is.True);
            Assert.That(hair.materialType, Is.EqualTo(UMAMaterial.MaterialType.UseExistingTextures));
            Assert.That(hair.generateMipMaps, Is.False);

            string path = AssetDatabase.GetAssetPath(hair);
            var speedBytes = File.ReadAllBytes(path);
            var speedTimestamp = File.GetLastWriteTimeUtc(path);
            UMAProjectDefaults.Apply(prefab, null, true, new[] { hair });
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(speedBytes));
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(speedTimestamp));

            // Force the backup to come from the saved asset, rather than the live object.
            Resources.UnloadAsset(hair);
            hair = AssetDatabase.LoadAssetAtPath<UMAMaterial>(path);
            Assert.That(UMAProjectDefaults.HasHairMaterialBackup(hair), Is.True);
            string result = UMAProjectDefaults.Apply(prefab, null, false, new[] { hair });
            Assert.That(result, Does.StartWith("Maximum Fidelity"));
            Assert.That(ReadPasses(hair), Is.EqualTo(original));
            Assert.That(UMAProjectDefaults.HasHairMaterialBackup(hair), Is.False);
            var restoredBytes = File.ReadAllBytes(path);
            UMAProjectDefaults.Apply(prefab, null, false, new[] { hair });
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(restoredBytes));
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(clip)), Is.EqualTo(clipBytes));
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(transparent)), Is.EqualTo(transparentBytes));
        }

        [Test]
        public void HairWithoutHdrpOverridesRetainsFallbacksAndCanCaptureANewBackupAfterRestore()
        {
            var clip = MakePass("HairClip", true);
            var transparent = MakePass("HairTransparent", false);
            var hair = MakeHair(clip, transparent);
            UMAProjectDefaults.Apply(prefab, null, false, new[] { hair });
            Assert.That(ReadPasses(hair), Is.EqualTo(new Material[] { clip, transparent, null, null }));
            UMAProjectDefaults.Apply(prefab, null, true, new[] { hair });
            Assert.That(ReadPasses(hair), Is.EqualTo(new Material[] { clip, null, null, null }));
            UMAProjectDefaults.Apply(prefab, null, false, new[] { hair });
            Assert.That(ReadPasses(hair), Is.EqualTo(new Material[] { clip, transparent, null, null }));

            // A deliberate edit after restoring fidelity must be the next cycle's backup.
            var replacement = MakePass("ReplacementHairTransparent", false);
            using (var serialized = new SerializedObject(hair))
            {
                serialized.FindProperty("_secondPass").objectReferenceValue = replacement;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            UMAProjectDefaults.Apply(prefab, null, true, new[] { hair });
            UMAProjectDefaults.Apply(prefab, null, false, new[] { hair });
            Assert.That(ReadPasses(hair), Is.EqualTo(new Material[] { clip, replacement, null, null }));
        }

        [Test]
        public void UnsupportedHairAndAlreadySingleClipHairArePreservedWithoutBackups()
        {
            var transparent = MakePass("HairTransparent", false);
            var hair = MakeHair(transparent);
            string path = AssetDatabase.GetAssetPath(hair);
            var before = File.ReadAllBytes(path);
            string result = UMAProjectDefaults.Apply(prefab, null, true, new[] { hair });
            Assert.That(result, Does.Contain("no compatible opaque Alpha Clip pass"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(UMAProjectDefaults.HasHairMaterialBackup(hair), Is.False);

            var clip = MakePass("HairClip", true);
            hair.material = clip;
            EditorUtility.SetDirty(hair);
            AssetDatabase.SaveAssetIfDirty(hair);
            before = File.ReadAllBytes(path);
            result = UMAProjectDefaults.Apply(prefab, null, true, new[] { hair });
            Assert.That(result, Does.Contain("already uses Alpha Clip only"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(UMAProjectDefaults.HasHairMaterialBackup(hair), Is.False);
        }

        [Test]
        public void UnknownPipelinePairIsNotDiscardedAndTransparentAlphaClipIsNotChosen()
        {
            var clip = MakePass("HairClip", true);
            var transparentClip = MakePass("HairTransparentClip", true);
            transparentClip.SetOverrideTag("RenderType", "Transparent");
            var hair = MakeHair(clip, transparentClip, transparentClip);
            var before = ReadPasses(hair);
            Assert.That(UMAProjectDefaults.CanUseAlphaClipHair(hair), Is.False);
            string result = UMAProjectDefaults.Apply(prefab, null, true, new[] { hair });
            Assert.That(result, Does.Contain("no compatible opaque Alpha Clip pass"));
            Assert.That(ReadPasses(hair), Is.EqualTo(before));
            Assert.That(UMAProjectDefaults.HasHairMaterialBackup(hair), Is.False);
        }

        [Test]
        public void HairDiscoveryChecksMaterialNamesAndBackupsWithoutIncludingClothing()
        {
            var clip = MakePass("HairClip", true);
            var transparent = MakePass("HairTransparent", false);
            var hair = MakeHair(clip, transparent);
            Assert.That(AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(hair), "Custom Cards"), Is.Empty);
            Assert.That(UMAProjectDefaults.IsHairMaterial(hair), Is.True);
            UMAProjectDefaults.Apply(prefab, null, true, new[] { hair });
            Assert.That(AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(clip), "Renamed Clip"), Is.Empty);
            Assert.That(UMAProjectDefaults.IsHairMaterial(hair), Is.True, "A preset backup must remain discoverable after renaming.");
            Assert.That(UMAProjectDefaults.IsHairMaterial(skin), Is.False);
            Assert.That(UMAProjectDefaults.IsHairMaterial(null), Is.False);
        }

        [Test]
        public void InvalidHairAssetIsRejectedBeforeGeneratorOrSkinChanges()
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            var before = File.ReadAllBytes(path);
            var transient = ScriptableObject.CreateInstance<UMAMaterial>();
            try
            {
                Assert.Throws<InvalidOperationException>(() => UMAProjectDefaults.Apply(prefab, new[] { skin }, true, new[] { transient }));
            }
            finally { Object.DestroyImmediate(transient); }
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(skin.materialType, Is.EqualTo(UMAMaterial.MaterialType.NoAtlas));
        }
    }
}
#endif
