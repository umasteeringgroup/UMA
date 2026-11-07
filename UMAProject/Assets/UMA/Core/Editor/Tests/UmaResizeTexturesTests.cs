#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UmaResizeTexturesTests
    {
        private string folder;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/UMAResizeTests-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(folder);

        private string CreateImage(string extension, int width = 64, int height = 32)
        {
            var texture = new Texture2D(width, height, extension == ".exr" ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false, true);
            try
            {
                var pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.25f, .5f, .75f, .25f);
                texture.SetPixels(pixels); texture.Apply();
                byte[] bytes = extension switch
                {
                    ".jpg" => texture.EncodeToJPG(95),
                    ".tga" => texture.EncodeToTGA(),
                    ".exr" => texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat),
                    _ => texture.EncodeToPNG()
                };
                string path = folder + "/source" + extension;
                File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.maxTextureSize = 128;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                return path;
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [TestCase(32)]
        [TestCase(64)]
        public void SourcesAtOrBelowMaximumAreByteForByteUnchanged(int maximum)
        {
            string path = CreateImage(".png", 32, 16);
            byte[] before = File.ReadAllBytes(path), meta = File.ReadAllBytes(path + ".meta");
            var result = UmaResizeTexturesUtility.Resize(AssetDatabase.AssetPathToGUID(path), maximum);
            Assert.That(result.CanResize, Is.False);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(meta));
        }

        [TestCase(".png")]
        [TestCase(".jpg")]
        [TestCase(".tga")]
        [TestCase(".exr")]
        public void LowersImporterWithoutRewritingSourceOrOtherSettings(string extension)
        {
            string path = CreateImage(extension);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.NormalMap;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Mirror;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
            string guid = AssetDatabase.AssetPathToGUID(path);
            byte[] source = File.ReadAllBytes(path);
            var result = UmaResizeTexturesUtility.Resize(guid, 32);
            Assert.That(result.CanResize, Is.True, result.Message);
            Assert.That(result.CurrentSize, Is.EqualTo(new Vector2Int(32, 16)));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(source));
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            Assert.That(new Vector2Int(width, height), Is.EqualTo(new Vector2Int(64, 32)));
            Assert.That(importer.maxTextureSize, Is.EqualTo(32));
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Mirror));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
        }

        [Test]
        public void SmallerExistingLimitIsNotRaised()
        {
            string path = CreateImage(".png", 128, 64);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = 32;
            importer.SaveAndReimport();
            byte[] meta = File.ReadAllBytes(path + ".meta");
            var result = UmaResizeTexturesUtility.Resize(AssetDatabase.AssetPathToGUID(path), 64);
            Assert.That(result.CanResize, Is.False);
            Assert.That(importer.maxTextureSize, Is.EqualTo(32));
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(meta));
        }

        [Test]
        public void LowersEnabledPlatformOverridesAndPreservesSmallerAndDisabledOverrides()
        {
            string path = CreateImage(".png", 256, 128);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            foreach (string name in new[] { "Standalone", "Android", "WebGL" })
            {
                var platform = importer.GetPlatformTextureSettings(name);
                platform.overridden = name != "WebGL";
                platform.maxTextureSize = name == "Android" ? 32 : 256;
                platform.compressionQuality = 37;
                importer.SetPlatformTextureSettings(platform);
            }
            importer.SaveAndReimport();
            var result = UmaResizeTexturesUtility.Resize(AssetDatabase.AssetPathToGUID(path), 64);
            Assert.That(result.CanResize, Is.True, result.Message);
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.maxTextureSize, Is.EqualTo(64));
            Assert.That(importer.GetPlatformTextureSettings("Standalone").maxTextureSize, Is.EqualTo(64));
            Assert.That(importer.GetPlatformTextureSettings("Standalone").compressionQuality, Is.EqualTo(37));
            Assert.That(importer.GetPlatformTextureSettings("Android").maxTextureSize, Is.EqualTo(32));
            Assert.That(importer.GetPlatformTextureSettings("WebGL").maxTextureSize, Is.EqualTo(256));
            Assert.That(importer.GetPlatformTextureSettings("WebGL").overridden, Is.False);
        }

        [Test]
        public void ReadOnlyImporterIsSkippedWithoutChangingBytes()
        {
            string path = CreateImage(".png"), metaPath = path + ".meta";
            byte[] before = File.ReadAllBytes(metaPath);
            FileAttributes original = File.GetAttributes(metaPath);
            try
            {
                File.SetAttributes(metaPath, original | FileAttributes.ReadOnly);
                var result = UmaResizeTexturesUtility.Resize(AssetDatabase.AssetPathToGUID(path), 32);
                Assert.That(result.CanResize, Is.False);
                Assert.That(result.Message, Does.Contain("read-only"));
                Assert.That(File.ReadAllBytes(metaPath), Is.EqualTo(before));
            }
            finally { File.SetAttributes(metaPath, original); }
        }

        [Test]
        public void NativeTextureAssetIsSkippedWithoutRewritingItsData()
        {
            string path = folder + "/native.asset";
            var texture = new Texture2D(64, 32);
            AssetDatabase.CreateAsset(texture, path);
            AssetDatabase.SaveAssetIfDirty(texture);
            byte[] before = File.ReadAllBytes(path);
            var result = UmaResizeTexturesUtility.Resize(AssetDatabase.AssetPathToGUID(path), 32);
            Assert.That(result.CanResize, Is.False);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void DialogHasLogicalDefaultAndDisablesAnEmptyOperation()
        {
            var window = ScriptableObject.CreateInstance<UmaResizeTexturesWindow>();
            try
            {
                window.CreateGUI();
                Assert.That(window.rootVisualElement.Q<DropdownField>("maximumSize").value, Is.EqualTo("1024"));
                Assert.That(window.rootVisualElement.Q<Button>("resize").enabledSelf, Is.False);
                Assert.That(window.rootVisualElement.Q<HelpBox>().text, Does.Contain("Only sizes down"));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void DialogChangesOnlySelectedImporterAndPreservesOriginalImages()
        {
            string path = CreateImage(".png"), untouched = CreateImage(".jpg");
            byte[] source = File.ReadAllBytes(path), otherMeta = File.ReadAllBytes(untouched + ".meta");
            var window = ScriptableObject.CreateInstance<UmaResizeTexturesWindow>();
            try
            {
                var serialized = new SerializedObject(window);
                var guids = serialized.FindProperty("textureGuids");
                guids.arraySize = 1;
                guids.GetArrayElementAtIndex(0).stringValue = AssetDatabase.AssetPathToGUID(path);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                window.ShowUtility();
                window.CreateGUI();
                window.rootVisualElement.Q<DropdownField>("maximumSize").value = "32";
                Assert.That(window.rootVisualElement.Q<Button>("resize").enabledSelf, Is.True,
                    window.rootVisualElement.Q<Label>("summary").text);
                Assert.That(window.rootVisualElement.Q<ScrollView>("entries").Q<Label>().text, Does.Contain("128 → 32"));
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(UmaResizeTexturesWindow).GetMethod("StartResize", flags).Invoke(window, null);
                typeof(UmaResizeTexturesWindow).GetMethod("ResizeNext", flags).Invoke(window, null);
                typeof(UmaResizeTexturesWindow).GetMethod("ResizeNext", flags).Invoke(window, null);
                Assert.That(window.rootVisualElement.Q<Label>("summary").text, Does.Contain("1 importers updated"));
                Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).maxTextureSize, Is.EqualTo(32));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(source));
                Assert.That(File.ReadAllBytes(untouched + ".meta"), Is.EqualTo(otherMeta));
            }
            finally { Object.DestroyImmediate(window); }
        }
    }
}
#endif
