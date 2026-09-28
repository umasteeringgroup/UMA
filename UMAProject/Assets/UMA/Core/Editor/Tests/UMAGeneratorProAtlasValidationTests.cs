#if UNITY_EDITOR

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAGeneratorProAtlasValidationTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        [Category("UMA")]
        [Category("Atlas")]
        [Category("AtlasDiagnostics")]
        public void OptimizeAtlasContinuesAfterEmptyGeneratedMaterial()
        {
            GameObject root = null;
            UMAMaterial emptyUmaMaterial = null;
            UMAMaterial populatedUmaMaterial = null;
            SlotDataAsset emptyAsset = null;
            SlotDataAsset populatedAsset = null;
            try
            {
                root = new GameObject("Generator Pro atlas test");
                UMAGenerator generator = root.AddComponent<UMAGenerator>();
                generator.atlasResolution = 512;
                var generatorPro = new UMAGeneratorPro();

                emptyUmaMaterial = CreateGeneratedUmaMaterial("Empty Atlas Material");
                populatedUmaMaterial = CreateGeneratedUmaMaterial("Populated Atlas Material");
                emptyAsset = CreateSlotAsset("EmptyThirdPartySlot");
                populatedAsset = CreateSlotAsset("FollowingValidSlot");

                UMAData.GeneratedMaterial empty = CreateGeneratedMaterial(
                    emptyUmaMaterial,
                    new SlotData(emptyAsset),
                    Rect.zero);
                UMAData.GeneratedMaterial populated = CreateGeneratedMaterial(
                    populatedUmaMaterial,
                    new SlotData(populatedAsset),
                    new Rect(0f, 0f, 128f, 64f));
                populated.cropResolution = new Vector2(512f, 512f);

                SetPrivateField(generatorPro, "umaGenerator", generator);
                List<UMAData.GeneratedMaterial> atlassed =
                    GetPrivateField<List<UMAData.GeneratedMaterial>>(
                        generatorPro, "atlassedMaterials");
                atlassed.Add(empty);
                atlassed.Add(populated);

                InvokePrivate(generatorPro, "OptimizeAtlas");

                Assert.AreEqual(Vector2.zero, empty.cropResolution);
                Assert.AreEqual(
                    new Vector2(128f, 64f),
                    populated.cropResolution,
                    "An empty material must not stop optimization of later generated materials.");
            }
            finally
            {
                Destroy(emptyAsset);
                Destroy(populatedAsset);
                Destroy(emptyUmaMaterial);
                Destroy(populatedUmaMaterial);
                Destroy(root);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("Atlas")]
        [Category("AtlasDiagnostics")]
        public void UpdateUvUsesIdentityFallbackForEmptyGeneratedMaterial()
        {
            GameObject root = null;
            UMAMaterial umaMaterial = null;
            SlotDataAsset asset = null;
            try
            {
                root = new GameObject("Generator Pro empty atlas fallback test");
                UMAGenerator generator = root.AddComponent<UMAGenerator>();
                generator.atlasResolution = 512;
                UMAData data = root.AddComponent<UMAData>();
                var generatorPro = new UMAGeneratorPro();

                umaMaterial = CreateGeneratedUmaMaterial("Third Party Empty Atlas Material");
                asset = CreateSlotAsset("Male_HelmetDome");
                var slot = new SlotData(asset)
                {
                    overlayScale = 0f,
                    UVArea = new Rect(0.2f, 0.3f, 0.4f, 0.5f)
                };
                UMAData.GeneratedMaterial generated = CreateGeneratedMaterial(
                    umaMaterial,
                    slot,
                    Rect.zero);
                generated.cropResolution = Vector2.zero;
                generated.resolutionScale = Vector2.one;
                data.generatedMaterials.materials.Add(generated);

                SetPrivateField(generatorPro, "umaGenerator", generator);
                SetPrivateField(generatorPro, "umaData", data);

                Assert.DoesNotThrow(() => InvokePrivate(generatorPro, "UpdateUV"));

                Assert.AreEqual(new Vector2(512f, 512f), generated.cropResolution);
                Assert.AreEqual(Vector2.one, generated.resolutionScale);
                Assert.AreEqual(
                    new Rect(0f, 0f, 512f, 512f),
                    generated.materialFragments[0].atlasRegion);
                Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), slot.UVArea);
                AssertRectIsFinite(generated.materialFragments[0].atlasRegion);
            }
            finally
            {
                Destroy(asset);
                Destroy(umaMaterial);
                Destroy(root);
            }
        }

        private static UMAMaterial CreateGeneratedUmaMaterial(string name)
        {
            UMAMaterial material = ScriptableObject.CreateInstance<UMAMaterial>();
            material.name = name;
            material.materialType = UMAMaterial.MaterialType.Atlas;
            material.channels = new UMAMaterial.MaterialChannel[1];
            return material;
        }

        private static SlotDataAsset CreateSlotAsset(string name)
        {
            SlotDataAsset asset = ScriptableObject.CreateInstance<SlotDataAsset>();
            asset.name = name;
            asset._oldSlotName = name;
            asset.overlayScale = 1f;
            return asset;
        }

        private static UMAData.GeneratedMaterial CreateGeneratedMaterial(
            UMAMaterial umaMaterial,
            SlotData slot,
            Rect atlasRegion)
        {
            var generated = new UMAData.GeneratedMaterial
            {
                umaMaterial = umaMaterial,
                cropResolution = Vector2.zero,
                resolutionScale = Vector2.one
            };
            generated.materialFragments.Add(new UMAData.MaterialFragment
            {
                slotData = slot,
                atlasRegion = atlasRegion,
                overlayList = new List<OverlayData>()
            });
            return generated;
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            Assert.NotNull(field, "Missing private field " + fieldName);
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            Assert.NotNull(field, "Missing private field " + fieldName);
            return (T)field.GetValue(target);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, PrivateInstance);
            Assert.NotNull(method, "Missing private method " + methodName);
            method.Invoke(target, null);
        }

        private static void AssertRectIsFinite(Rect rect)
        {
            Assert.IsFalse(float.IsNaN(rect.x));
            Assert.IsFalse(float.IsNaN(rect.y));
            Assert.IsFalse(float.IsNaN(rect.width));
            Assert.IsFalse(float.IsNaN(rect.height));
            Assert.IsFalse(float.IsInfinity(rect.x));
            Assert.IsFalse(float.IsInfinity(rect.y));
            Assert.IsFalse(float.IsInfinity(rect.width));
            Assert.IsFalse(float.IsInfinity(rect.height));
        }

        private static void Destroy(Object value)
        {
            if (value != null)
            {
                Object.DestroyImmediate(value);
            }
        }
    }
}

#endif
