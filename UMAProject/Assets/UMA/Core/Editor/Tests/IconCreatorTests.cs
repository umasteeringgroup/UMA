#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
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
        public void CaptureSupersamplingIsClampedBetweenOneAndFour()
        {
            GameObject gameObject = new GameObject("Icon Creator supersampling test");
            try
            {
                IconCreator iconCreator = gameObject.AddComponent<IconCreator>();
                iconCreator.CaptureSupersampling = 8;

                InvokePrivate(iconCreator, "OnValidate");

                Assert.AreEqual(4, iconCreator.CaptureSupersampling);

                iconCreator.CaptureSupersampling = 0;
                InvokePrivate(iconCreator, "OnValidate");

                Assert.AreEqual(1, iconCreator.CaptureSupersampling);
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
                camera.cullingMask = 0;
                renderTexture = new RenderTexture(32, 24, 24, RenderTextureFormat.ARGB32);
                renderTexture.Create();
                camera.targetTexture = renderTexture;

                IconCreator iconCreator = gameObject.AddComponent<IconCreator>();
                iconCreator.IconDimensions = new Vector2(19f, 11f);
                iconCreator.CaptureSupersampling = 2;

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
        public void TemporaryCameraCaptureTargetHasDepthStencilBuffer()
        {
            RenderTexture captureTexture = null;
            try
            {
                captureTexture = (RenderTexture)InvokePrivateStatic(
                    typeof(IconCreator),
                    "GetTemporaryCameraCaptureTexture",
                    19,
                    11);

                Assert.IsNotNull(captureTexture);
                Assert.AreEqual(19, captureTexture.width);
                Assert.AreEqual(11, captureTexture.height);
                Assert.Greater(captureTexture.depth, 0);
            }
            finally
            {
                if (captureTexture != null)
                {
                    RenderTexture.ReleaseTemporary(captureTexture);
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

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void ExistingThumbnailPathIsPreservedForMatchingRace()
        {
            string folderName = "IconCreatorTests_" + Guid.NewGuid().ToString("N");
            string assetFolder = "Assets/" + folderName;
            string absoluteFolder = Path.Combine(Application.dataPath, folderName);
            string assetPath = assetFolder + "/7_ExistingThumbnail.png";
            string absolutePath = Path.Combine(absoluteFolder, "7_ExistingThumbnail.png");
            GameObject gameObject = new GameObject("Icon Creator existing path test");
            UMAWardrobeRecipe recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                AssetDatabase.CreateFolder("Assets", folderName);
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());

                recipe.name = "Test Recipe";
                recipe.wardrobeRecipeThumbs.Add(new WardrobeRecipeThumb
                {
                    race = "Test Race",
                    filename = assetPath
                });

                IconCreator iconCreator = gameObject.AddComponent<IconCreator>();
                string outputPath = (string)InvokePrivate(
                    iconCreator,
                    "GetThumbnailOutputPath",
                    recipe,
                    "Test Race",
                    absoluteFolder);

                Assert.AreEqual(NormalizePath(absolutePath), NormalizePath(outputPath));

                string otherRaceOutputPath = (string)InvokePrivate(
                    iconCreator,
                    "GetThumbnailOutputPath",
                    recipe,
                    "Other Race",
                    absoluteFolder);

                Assert.AreNotEqual(NormalizePath(absolutePath), NormalizePath(otherRaceOutputPath));
                Assert.AreEqual(
                    NormalizePath(absoluteFolder),
                    NormalizePath(Path.GetDirectoryName(otherRaceOutputPath)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(recipe);
                UnityEngine.Object.DestroyImmediate(gameObject);
                AssetDatabase.DeleteAsset(assetFolder);
                if (Directory.Exists(absoluteFolder))
                {
                    Directory.Delete(absoluteFolder, true);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void DetachedRecipeDoesNotRetainThumbnailAssets()
        {
            UMAWardrobeRecipe sourceRecipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            UMAWardrobeRecipe detachedRecipe = null;
            Texture2D texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
            try
            {
                sourceRecipe.name = "Detached Recipe Test";
                sourceRecipe.recipeString = "serialized recipe data";
                sourceRecipe.thumbnailFromTexture = true;
                sourceRecipe.wardrobeRecipeThumbs.Add(new WardrobeRecipeThumb("Test Race", sprite));

                detachedRecipe = (UMAWardrobeRecipe)InvokePrivateStatic(
                    typeof(IconCreator),
                    "CreateDetachedRecipe",
                    sourceRecipe);

                Assert.AreNotSame(sourceRecipe, detachedRecipe);
                Assert.AreEqual(sourceRecipe.name, detachedRecipe.name);
                Assert.AreEqual(sourceRecipe.recipeString, detachedRecipe.recipeString);
                Assert.AreEqual(sourceRecipe.thumbnailFromTexture, detachedRecipe.thumbnailFromTexture);
                Assert.IsNotNull(detachedRecipe.wardrobeRecipeThumbs);
                Assert.IsEmpty(detachedRecipe.wardrobeRecipeThumbs);
                Assert.AreEqual(HideFlags.HideAndDontSave, detachedRecipe.hideFlags);
                Assert.AreSame(sprite, sourceRecipe.wardrobeRecipeThumbs[0].thumb);
            }
            finally
            {
                if (detachedRecipe != null)
                {
                    UnityEngine.Object.DestroyImmediate(detachedRecipe);
                }
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(sourceRecipe);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void ThumbnailWriteOverwritesImportedTextureWithoutChangingGuid()
        {
            string folderName = "IconCreatorTests_" + Guid.NewGuid().ToString("N");
            string assetFolder = "Assets/" + folderName;
            string absoluteFolder = Path.Combine(Application.dataPath, folderName);
            string assetPath = assetFolder + "/ExistingThumbnail.png";
            string absolutePath = Path.Combine(absoluteFolder, "ExistingThumbnail.png");
            Texture2D initialTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D replacementTexture = new Texture2D(8, 4, TextureFormat.RGBA32, false);
            UMAWardrobeRecipe recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            FileStream thumbnailLock = null;
            System.Threading.Tasks.Task releaseLock = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folderName);
                File.WriteAllBytes(absolutePath, initialTexture.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                TextureImporter textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                Assert.IsNotNull(textureImporter);
                textureImporter.textureType = TextureImporterType.Sprite;
                textureImporter.spriteImportMode = SpriteImportMode.Single;
                textureImporter.SaveAndReimport();

                string originalGuid = AssetDatabase.AssetPathToGUID(assetPath);
                Assert.IsNotEmpty(originalGuid);
                Sprite importedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                Assert.IsNotNull(importedSprite);
                Assert.IsNotNull(importedSprite.texture);
                recipe.wardrobeRecipeThumbs.Add(new WardrobeRecipeThumb("Test Race", importedSprite));

                byte[] replacementBytes = replacementTexture.EncodeToPNG();
                recipe.wardrobeRecipeThumbs[0].thumb = null;
                InvokePrivateStatic(
                    typeof(IconCreator),
                    "UnloadThumbnailAsset",
                    assetPath);
                importedSprite = null;
                EditorUtility.UnloadUnusedAssetsImmediate(false);

                thumbnailLock = File.Open(
                    absolutePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                FileStream lockToRelease = thumbnailLock;
                releaseLock = System.Threading.Tasks.Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(100);
                    lockToRelease.Dispose();
                });
                InvokePrivateStatic(
                    typeof(IconCreator),
                    "WriteThumbnailFile",
                    absolutePath,
                    replacementBytes);
                releaseLock.Wait();
                Assert.IsEmpty(Directory.GetFiles(absoluteFolder, "*.tmp"));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                importedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                Assert.IsNotNull(importedSprite);
                Assert.AreEqual(8, importedSprite.texture.width);
                Assert.AreEqual(4, importedSprite.texture.height);
                Assert.AreEqual(originalGuid, AssetDatabase.AssetPathToGUID(assetPath));
            }
            finally
            {
                thumbnailLock?.Dispose();
                releaseLock?.Wait(1000);
                if (recipe != null)
                {
                    UnityEngine.Object.DestroyImmediate(recipe);
                }
                if (replacementTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(replacementTexture);
                }
                if (initialTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(initialTexture);
                }
                AssetDatabase.DeleteAsset(assetFolder);
                if (Directory.Exists(absoluteFolder))
                {
                    Directory.Delete(absoluteFolder, true);
                }
            }
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).Replace('\\', '/');
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
