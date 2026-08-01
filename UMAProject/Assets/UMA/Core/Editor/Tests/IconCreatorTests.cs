#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class IconCreatorTests
    {
        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void IconDimensionsAreClampedToPositiveIntegers()
        {
            GameObject gameObject = new GameObject("Icon Creator dimension test");
            try
            {
                IconCreator iconCreator = gameObject.AddComponent<IconCreator>();
                iconCreator.IconDimensions = new Vector2(-12.4f, 128.6f);

                InvokePrivate(iconCreator, "OnValidate");

                Assert.AreEqual(new Vector2(1f, 129f), iconCreator.IconDimensions);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void CameraCaptureUsesIconDimensions()
        {
            GameObject gameObject = new GameObject("Icon Creator capture test");
            RenderTexture renderTexture = null;
            Texture2D loadedTexture = null;
            string outputPath = Path.Combine(
                Path.GetTempPath(),
                "UMAIconCreator_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                Camera camera = gameObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.1f, 0.7f, 0.2f, 1f);
                renderTexture = new RenderTexture(32, 24, 0, RenderTextureFormat.ARGB32);
                renderTexture.Create();
                camera.targetTexture = renderTexture;

                IconCreator iconCreator = gameObject.AddComponent<IconCreator>();
                iconCreator.IconDimensions = new Vector2(19f, 11f);

                bool captured = (bool)InvokePrivate(
                    iconCreator,
                    "CaptureRenderTextureToPng",
                    camera,
                    outputPath);

                Assert.IsTrue(captured);
                Assert.IsTrue(File.Exists(outputPath));

                loadedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Assert.IsTrue(loadedTexture.LoadImage(File.ReadAllBytes(outputPath)));
                Assert.AreEqual(19, loadedTexture.width);
                Assert.AreEqual(11, loadedTexture.height);
                Assert.AreSame(renderTexture, camera.targetTexture);

                Color centerPixel = loadedTexture.GetPixel(loadedTexture.width / 2, loadedTexture.height / 2);
                Assert.Greater(centerPixel.g, centerPixel.r);
                Assert.Greater(centerPixel.g, centerPixel.b);
            }
            finally
            {
                if (loadedTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(loadedTexture);
                }
                if (renderTexture != null)
                {
                    Camera camera = gameObject.GetComponent<Camera>();
                    if (camera != null)
                    {
                        camera.targetTexture = null;
                    }
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void TextureCropCanBeResampledToRequestedDimensions()
        {
            Texture2D sourceTexture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            Texture2D outputTexture = null;
            try
            {
                outputTexture = (Texture2D)InvokePrivateStatic(
                    typeof(IconCreator),
                    "GetReadableTexture2D",
                    sourceTexture,
                    new Rect(0.25f, 0.25f, 0.5f, 0.5f),
                    13,
                    7);

                Assert.IsNotNull(outputTexture);
                Assert.AreEqual(13, outputTexture.width);
                Assert.AreEqual(7, outputTexture.height);
            }
            finally
            {
                if (outputTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(outputTexture);
                }
                UnityEngine.Object.DestroyImmediate(sourceTexture);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void TextureDerivedThumbnailPreservesNativeCropDimensionsByDefault()
        {
            GameObject gameObject = new GameObject("Icon Creator texture dimension test");
            Texture2D sourceTexture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            try
            {
                IconCreator iconCreator = gameObject.AddComponent<IconCreator>();
                iconCreator.IconDimensions = new Vector2(160f, 90f);

                Vector2Int dimensions = (Vector2Int)InvokePrivate(
                    iconCreator,
                    "GetTextureThumbnailDimensions",
                    sourceTexture,
                    new Rect(0.25f, 0.25f, 0.5f, 0.5f));

                Assert.AreEqual(new Vector2Int(512, 512), dimensions);

                iconCreator.ResizeTextureDerivedThumbnails = true;
                dimensions = (Vector2Int)InvokePrivate(
                    iconCreator,
                    "GetTextureThumbnailDimensions",
                    sourceTexture,
                    new Rect(0.25f, 0.25f, 0.5f, 0.5f));

                Assert.AreEqual(new Vector2Int(160, 90), dimensions);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceTexture);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static object InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing method: " + methodName);
            return method.Invoke(target, arguments);
        }

        private static object InvokePrivateStatic(
            Type targetType,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = targetType.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing method: " + methodName);
            return method.Invoke(null, arguments);
        }
    }

}
#endif
