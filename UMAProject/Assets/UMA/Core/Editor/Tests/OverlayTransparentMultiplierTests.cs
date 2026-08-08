using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    public class OverlayTransparentMultiplierTests
    {
        [Test]
        public void NewOverlayDefaultsToClearTransparentMultiplier()
        {
            OverlayDataAsset asset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                var overlay = new OverlayData(asset);

                Assert.AreEqual(Color.clear, overlay.TransparentMultiplier);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void DuplicateCopiesTransparentMultiplier()
        {
            OverlayDataAsset asset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                var overlay = new OverlayData(asset)
                {
                    TransparentMultiplier = new Color(0.2f, 0.4f, 0.6f, 0.8f)
                };

                OverlayData duplicate = overlay.Duplicate();

                Assert.AreEqual(
                    overlay.TransparentMultiplier,
                    duplicate.TransparentMultiplier);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void EquivalentIncludesTransparentMultiplier()
        {
            OverlayDataAsset asset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                var first = new OverlayData(asset);
                var second = new OverlayData(asset)
                {
                    colorData = first.colorData,
                    rect = first.rect,
                    TransparentMultiplier = Color.red
                };

                first.TransparentMultiplier = Color.blue;
                Assert.IsFalse(OverlayData.Equivalent(first, second));

                second.TransparentMultiplier = first.TransparentMultiplier;
                Assert.IsTrue(OverlayData.Equivalent(first, second));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void PackRecipeV3StoresTransparentMultiplier()
        {
            SlotDataAsset slotAsset =
                ScriptableObject.CreateInstance<SlotDataAsset>();
            OverlayDataAsset overlayAsset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                slotAsset._oldSlotName = "TransparentMultiplierTestSlot";
                overlayAsset._oldOverlayName =
                    "TransparentMultiplierTestOverlay";
                overlayAsset.textureList = Array.Empty<Texture>();

                var overlay = new OverlayData(overlayAsset)
                {
                    TransparentMultiplier =
                        new Color(0.25f, 0.5f, 0.75f, 0.6f)
                };
                var slot = new SlotData(slotAsset);
                slot.AddOverlay(overlay);

                var recipe = new UMAData.UMARecipe
                {
                    slotDataList = new[] { slot },
                    sharedColors = Array.Empty<OverlayColorData>()
                };

                UMAPackedRecipeBase.UMAPackRecipe packed =
                    UMAPackedRecipeBase.PackRecipeV3(recipe);
                UMAPackedRecipeBase.PackedOverlayDataV3 packedOverlay =
                    packed.slotsV3[0].overlays[0];

                Assert.IsTrue(packedOverlay.hasTransparentMultiplier);
                Assert.AreEqual(
                    overlay.TransparentMultiplier,
                    packedOverlay.transparentMultiplier);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }

        [Test]
        public void LegacyPackedOverlayUsesClearTransparentMultiplier()
        {
            OverlayDataAsset asset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                var overlay = new OverlayData(asset)
                {
                    TransparentMultiplier = Color.black
                };
                var packed =
                    new UMAPackedRecipeBase.PackedOverlayDataV3
                    {
                        hasTransparentMultiplier = false,
                        transparentMultiplier = Color.red
                    };

                MethodInfo applyMethod = typeof(UMAPackedRecipeBase).GetMethod(
                    "ApplyPackedTransparentMultiplier",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(applyMethod);
                applyMethod.Invoke(null, new object[] { overlay, packed });

                Assert.AreEqual(Color.clear, overlay.TransparentMultiplier);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void DrawAllRectsPrefillsOnlyQueuedRectanglesInStackOrder()
        {
            Shader fillShader = Shader.Find("Hidden/UMA/TransparentPrefill");
            Assert.IsNotNull(fillShader);

            TextureMerge textureMerge =
                ScriptableObject.CreateInstance<TextureMerge>();
            Material sourceMaterial = new Material(fillShader);
            RenderTexture target = null;
            Texture2D readback = null;

            try
            {
                textureMerge.material = sourceMaterial;
                textureMerge.transparentPrefillShader = fillShader;
                textureMerge.EnsurePreviewRectCapacity(2);

                TextureMerge.TextureMergeRect[] rects =
                    textureMerge.GetPreviewRects();
                rects[0].rect = new Rect(1f, 1f, 5f, 5f);
                rects[0].transparentPrefill = true;
                rects[0].transparentPrefillColor =
                    new Color(1f, 0f, 0f, 0f);
                rects[0].transform = false;
                rects[0].tex = null;

                rects[1].rect = new Rect(3f, 3f, 2f, 2f);
                rects[1].transparentPrefill = true;
                rects[1].transparentPrefillColor =
                    new Color(0f, 1f, 0f, 0f);
                rects[1].transform = false;
                rects[1].tex = null;
                textureMerge.SetPreviewRectCount(2);

                target = new RenderTexture(
                    8,
                    8,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear)
                {
                    filterMode = FilterMode.Point
                };
                target.Create();

                textureMerge.DrawAllRects(
                    target,
                    target.width,
                    target.height,
                    new Color(0f, 0f, 1f, 0f),
                    false);

                readback = new Texture2D(
                    target.width,
                    target.height,
                    TextureFormat.RGBA32,
                    false,
                    true);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                readback.ReadPixels(
                    new Rect(0f, 0f, target.width, target.height),
                    0,
                    0);
                readback.Apply(false, false);
                RenderTexture.active = previous;

                AssertColorApproximately(
                    Color.blue,
                    readback.GetPixel(0, 0));
                AssertColorApproximately(
                    Color.red,
                    readback.GetPixel(2, 2));
                AssertColorApproximately(
                    Color.green,
                    readback.GetPixel(3, 3));
                Assert.That(readback.GetPixel(3, 3).a, Is.EqualTo(0f).Within(0.01f));
            }
            finally
            {
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
                if (readback != null)
                {
                    UnityEngine.Object.DestroyImmediate(readback);
                }
                UnityEngine.Object.DestroyImmediate(textureMerge);
                UnityEngine.Object.DestroyImmediate(sourceMaterial);
            }
        }

        private static void AssertColorApproximately(
            Color expected,
            Color actual)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.01f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.01f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.01f));
        }
    }
}
