#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace UMA.Editors.Tests
{
    public sealed class IconCreatorSpriteAtlasUtilityTests
    {
        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void AtlasFolderIsCreatedUnderTheIconRoot()
        {
            Assert.AreEqual(
                "Assets/UMA/UMA3/Wearables/Icons/SpriteAtlases",
                IconCreatorSpriteAtlasUtility.GetAtlasFolder(
                    "Assets/UMA/UMA3/Wearables/Icons"));
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void AtlasFolderRejectsPathsOutsideAssets()
        {
            Assert.Throws<ArgumentException>(() =>
                IconCreatorSpriteAtlasUtility.GetAtlasFolder(Path.GetTempPath()));
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void SpriteAtlasV2ModesAreDetected()
        {
            Assert.IsTrue((bool)InvokePrivateStatic(
                "IsSpriteAtlasV2Enabled",
                SpritePackerMode.SpriteAtlasV2));
            Assert.IsTrue((bool)InvokePrivateStatic(
                "IsSpriteAtlasV2Enabled",
                SpritePackerMode.SpriteAtlasV2Build));
            Assert.IsFalse((bool)InvokePrivateStatic(
                "IsSpriteAtlasV2Enabled",
                SpritePackerMode.AlwaysOnAtlas));
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void SpriteAtlasV2RebuildReusesAssetAndReplacesPackables()
        {
            string folder = CreateTestFolder();
            string atlasPath = folder + "/UMAIcons_TestRace_TestRegion.spriteatlasv2";
            try
            {
                Sprite firstSprite = CreateSpriteAsset(folder, "First");
                Sprite secondSprite = CreateSpriteAsset(folder, "Second");

                InvokePrivateStatic(
                    "RebuildAtlas",
                    atlasPath,
                    new List<Sprite> { firstSprite, secondSprite },
                    true);

                string originalGuid = AssetDatabase.AssetPathToGUID(atlasPath);
                Assert.IsNotEmpty(originalGuid);
                Assert.AreEqual(2, GetSpriteAtlasV2PackableCount(atlasPath));

                InvokePrivateStatic(
                    "RebuildAtlas",
                    atlasPath,
                    new List<Sprite> { secondSprite },
                    true);

                Assert.AreEqual(originalGuid, AssetDatabase.AssetPathToGUID(atlasPath));
                Assert.AreEqual(1, GetSpriteAtlasV2PackableCount(atlasPath));

                SpriteAtlasImporter importer =
                    AssetImporter.GetAtPath(atlasPath) as SpriteAtlasImporter;
                Assert.IsNotNull(importer);
                Assert.IsTrue(importer.includeInBuild);
                Assert.AreEqual(4, importer.packingSettings.padding);
                Assert.IsFalse(importer.packingSettings.enableRotation);
                Assert.IsFalse(importer.textureSettings.generateMipMaps);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void SpriteAtlasV2RebuildRecreatesDeletedAssetWithCachedGuid()
        {
            string folder = CreateTestFolder();
            string atlasPath = folder + "/UMAIcons_Deleted_TestRegion.spriteatlasv2";
            try
            {
                Sprite sprite = CreateSpriteAsset(folder, "Replacement");
                var originalAtlas = new SpriteAtlasAsset();
                try
                {
                    SpriteAtlasAsset.Save(originalAtlas, atlasPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(originalAtlas);
                }
                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);

                Assert.IsTrue(AssetDatabase.DeleteAsset(atlasPath));
                Assert.IsFalse(File.Exists(Path.GetFullPath(atlasPath)));

                InvokePrivateStatic(
                    "RebuildAtlas",
                    atlasPath,
                    new List<Sprite> { sprite },
                    true);

                Assert.IsTrue(File.Exists(Path.GetFullPath(atlasPath)));
                Assert.AreEqual(1, GetSpriteAtlasV2PackableCount(atlasPath));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void InactiveGeneratedAtlasVersionIsRemoved()
        {
            string folder = CreateTestFolder();
            string atlasName = "/UMAIcons_TestRace_TestRegion";
            string v1Path = folder + atlasName + ".spriteatlas";
            string v2Path = folder + atlasName + ".spriteatlasv2";
            try
            {
                AssetDatabase.CreateAsset(new SpriteAtlas(), v1Path);
                var v2Atlas = new SpriteAtlasAsset();
                try
                {
                    SpriteAtlasAsset.Save(v2Atlas, v2Path);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(v2Atlas);
                }
                AssetDatabase.ImportAsset(v2Path, ImportAssetOptions.ForceUpdate);

                int removedCount = (int)InvokePrivateStatic(
                    "RemoveInactiveAtlasAssets",
                    folder,
                    true);

                Assert.AreEqual(1, removedCount);
                Assert.IsFalse(File.Exists(Path.GetFullPath(v1Path)));
                Assert.IsTrue(File.Exists(Path.GetFullPath(v2Path)));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static string CreateTestFolder()
        {
            string folderName = "IconCreatorAtlasTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            return "Assets/" + folderName;
        }

        private static Sprite CreateSpriteAsset(string folder, string name)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = name + "Texture"
            };
            string texturePath = folder + "/" + name + ".asset";
            AssetDatabase.CreateAsset(texture, texturePath);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
            sprite.name = name;
            AssetDatabase.AddObjectToAsset(sprite, texture);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
            return sprite;
        }

        private static int GetSpriteAtlasV2PackableCount(string atlasPath)
        {
            SpriteAtlasAsset atlas = SpriteAtlasAsset.Load(atlasPath);
            try
            {
                var serializedAtlas = new SerializedObject(atlas);
                SerializedProperty packables =
                    serializedAtlas.FindProperty("m_ImporterData.packables");
                Assert.IsNotNull(packables);
                return packables.arraySize;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(atlas);
            }
        }

        private static object InvokePrivateStatic(string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(IconCreatorSpriteAtlasUtility).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing method: " + methodName);
            return method.Invoke(null, arguments);
        }
    }
}
#endif
