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
        public void RecipeTextureIsResampledToIconDimensions()
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
