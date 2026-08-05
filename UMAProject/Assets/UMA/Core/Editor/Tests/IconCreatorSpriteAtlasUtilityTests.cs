#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;

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
        public void ThumbnailSourcesAreRestrictedToTheConfiguredIconRoot()
        {
            const string iconRoot = "Assets/UMA/UMA3/Wearables/Icons";
            const string atlasFolder = iconRoot + "/SpriteAtlases";

            Assert.IsTrue((bool)InvokePrivateStatic(
                "IsThumbnailSourcePath",
                iconRoot + "/Hair/Human Female 3.0/Hair.png",
                iconRoot,
                atlasFolder));
            Assert.IsFalse((bool)InvokePrivateStatic(
                "IsThumbnailSourcePath",
                "Assets/UMA2/Wearables/Thumbs/Hair.png",
                iconRoot,
                atlasFolder));
            Assert.IsFalse((bool)InvokePrivateStatic(
                "IsThumbnailSourcePath",
                "Assets/UMA/UMA3/Wearables/IconsLegacy/Hair.png",
                iconRoot,
                atlasFolder));
            Assert.IsFalse((bool)InvokePrivateStatic(
                "IsThumbnailSourcePath",
                atlasFolder + "/Generated.png",
                iconRoot,
                atlasFolder));
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void AtlasGroupsOnlyIncludeThumbnailSpritesUnderTheConfiguredRoot()
        {
            string folder = CreateTestFolder();
            string iconRoot = folder + "/Icons";
            string externalFolder = folder + "/External";
            AssetDatabase.CreateFolder(folder, "Icons");
            AssetDatabase.CreateFolder(folder, "External");
            try
            {
                Sprite includedSprite = CreateSpriteAsset(iconRoot, "Included");
                Sprite excludedSprite = CreateSpriteAsset(externalFolder, "Excluded");
                CreateWardrobeRecipeAsset(
                    folder,
                    "IncludedRecipe",
                    "IncludedRegion",
                    "IncludedRace",
                    includedSprite);
                CreateWardrobeRecipeAsset(
                    folder,
                    "ExcludedRecipe",
                    "ExcludedRegion",
                    "ExcludedRace",
                    excludedSprite);
                AssetDatabase.SaveAssets();

                Type groupKeyType = typeof(IconCreatorSpriteAtlasUtility).GetNestedType(
                    "AtlasGroupKey",
                    BindingFlags.NonPublic);
                Assert.IsNotNull(groupKeyType);
                Type assignmentsType = typeof(Dictionary<,>).MakeGenericType(
                    typeof(string),
                    groupKeyType);
                IDictionary assignments = (IDictionary)Activator.CreateInstance(assignmentsType);
                var warnings = new List<string>();
                object[] arguments =
                {
                    iconRoot,
                    iconRoot + "/SpriteAtlases",
                    assignments,
                    warnings,
                    0
                };

                MethodInfo collectGroups = typeof(IconCreatorSpriteAtlasUtility).GetMethod(
                    "CollectGroups",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(collectGroups);
                IDictionary groups = (IDictionary)collectGroups.Invoke(null, arguments);

                Assert.AreEqual(1, groups.Count);
                Assert.AreEqual(1, assignments.Count);
                Assert.AreEqual(1, (int)arguments[4]);
                Assert.AreEqual(0, warnings.Count);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void SpriteAtlasV2ModesAreDetected()
        {
            Assert.IsTrue((bool)InvokePrivateStatic(
                "IsSpriteAtlasV2Enabled",
                SpritePackerMode.SpriteAtlasV2));
            Assert.IsFalse((bool)InvokePrivateStatic(
                "IsSpriteAtlasV2Enabled",
                SpritePackerMode.SpriteAtlasV2Build));
            Assert.IsFalse((bool)InvokePrivateStatic(
                "IsSpriteAtlasV2Enabled",
                SpritePackerMode.AlwaysOnAtlas));
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void AtlasRebuildRequiresSpriteAtlasV2EnabledInTheEditor()
        {
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
                InvokePrivateStatic(
                    "EnsureSpriteAtlasV2Enabled",
                    SpritePackerMode.SpriteAtlasV2Build));

            Assert.IsInstanceOf<InvalidOperationException>(exception.InnerException);
            StringAssert.Contains(
                "Sprite Atlas V2 - Enabled",
                exception.InnerException.Message);
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
                    new List<Sprite> { firstSprite, secondSprite });

                string originalGuid = AssetDatabase.AssetPathToGUID(atlasPath);
                Assert.IsNotEmpty(originalGuid);
                Assert.AreEqual(2, GetSpriteAtlasV2PackableCount(atlasPath));

                InvokePrivateStatic(
                    "RebuildAtlas",
                    atlasPath,
                    new List<Sprite> { secondSprite });

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
        public void SpriteAtlasV2RebuildRecreatesDeletedAsset()
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
                    new List<Sprite> { sprite });

                Assert.IsTrue(File.Exists(Path.GetFullPath(atlasPath)));
                Assert.AreEqual(1, GetSpriteAtlasV2PackableCount(atlasPath));
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

        private static void CreateWardrobeRecipeAsset(
            string folder,
            string name,
            string region,
            string race,
            Sprite sprite)
        {
            UMAWardrobeRecipe recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            recipe.name = name;
            recipe.wardrobeSlot = region;
            recipe.wardrobeRecipeThumbs.Add(new WardrobeRecipeThumb(race, sprite));
            AssetDatabase.CreateAsset(recipe, folder + "/" + name + ".asset");
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
