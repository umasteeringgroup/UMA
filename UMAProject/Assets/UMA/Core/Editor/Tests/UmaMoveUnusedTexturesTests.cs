#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UmaMoveUnusedTexturesTests
    {
        private string _rootFolder;
        private string _sourceFolder;
        private string _destinationFolder;

        [SetUp]
        public void SetUp()
        {
            _rootFolder = "Assets/__UMAMoveUnusedTexturesTests_" +
                Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder(
                "Assets", _rootFolder.Substring("Assets/".Length));
            AssetDatabase.CreateFolder(_rootFolder, "Source");
            AssetDatabase.CreateFolder(_rootFolder, "Destination");
            _sourceFolder = _rootFolder + "/Source";
            _destinationFolder = _rootFolder + "/Destination";
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_rootFolder) &&
                AssetDatabase.IsValidFolder(_rootFolder))
            {
                AssetDatabase.DeleteAsset(_rootFolder);
            }
            AssetDatabase.Refresh();
        }

        [Test]
        public void TextureListReferenceKeepsTextureAndReportsOverlay()
        {
            Texture2D texture = CreateTexture(_sourceFolder, "Body.asset");
            OverlayDataAsset overlay = CreateOverlay("BodyOverlay.asset");
            overlay.textureList = new Texture[] { texture };
            EditorUtility.SetDirty(overlay);
            AssetDatabase.SaveAssetIfDirty(overlay);

            List<UmaMoveUnusedTextureResult> results =
                UmaMoveUnusedTexturesUtility.ProcessTextures(
                    new[] { texture }, _destinationFolder,
                    new[] { overlay });

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(UmaMoveUnusedTextureStatus.FoundInOverlay,
                results[0].Status);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(
                _sourceFolder + "/Body.asset"));
            Assert.That(results[0].Details,
                Has.Some.Contains("textureList[0]"));
            Assert.That(results[0].Details,
                Has.Some.Contains("BodyOverlay.asset"));
        }

        [Test]
        public void AlphaMaskReferenceKeepsTextureAndReportsAlphaMask()
        {
            Texture2D texture = CreateTexture(_sourceFolder, "Mask.asset");
            OverlayDataAsset overlay = CreateOverlay("MaskedOverlay.asset");
            overlay.textureList = Array.Empty<Texture>();
            overlay.textureNames = Array.Empty<string>();
            overlay.alphaMask = texture;
            EditorUtility.SetDirty(overlay);
            AssetDatabase.SaveAssetIfDirty(overlay);

            List<UmaMoveUnusedTextureResult> results =
                UmaMoveUnusedTexturesUtility.ProcessTextures(
                    new[] { texture }, _destinationFolder,
                    new[] { overlay });

            Assert.AreEqual(UmaMoveUnusedTextureStatus.FoundInOverlay,
                results[0].Status);
            Assert.That(results[0].Details,
                Has.Some.Contains("alphaMask"));
        }

        [Test]
        public void NameOnlyReferenceKeepsStrippedTexture()
        {
            Texture2D texture = CreateTexture(_sourceFolder, "Stripped.asset");
            OverlayDataAsset overlay = CreateOverlay("AddressableOverlay.asset");
            overlay.textureList = new Texture[] { null };
            overlay.textureNames = new[] { texture.name };
            EditorUtility.SetDirty(overlay);
            AssetDatabase.SaveAssetIfDirty(overlay);

            List<UmaMoveUnusedTextureResult> results =
                UmaMoveUnusedTexturesUtility.ProcessTextures(
                    new[] { texture }, _destinationFolder,
                    new[] { overlay });

            Assert.AreEqual(UmaMoveUnusedTextureStatus.FoundInOverlay,
                results[0].Status);
            Assert.That(results[0].Details,
                Has.Some.Contains("name-only reference"));
        }

        [Test]
        public void UnreferencedTextureMovesAndPreservesGuid()
        {
            Texture2D texture = CreateTexture(_sourceFolder, "Unused.asset");
            string sourcePath = _sourceFolder + "/Unused.asset";
            string destinationPath = _destinationFolder + "/Unused.asset";
            string guid = AssetDatabase.AssetPathToGUID(sourcePath);

            List<UmaMoveUnusedTextureResult> results =
                UmaMoveUnusedTexturesUtility.ProcessTextures(
                    new[] { texture }, _destinationFolder,
                    Array.Empty<OverlayDataAsset>());

            Assert.AreEqual(UmaMoveUnusedTextureStatus.Moved,
                results[0].Status);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(
                destinationPath));
            Assert.AreEqual(destinationPath,
                AssetDatabase.GUIDToAssetPath(guid));
        }

        [Test]
        public void ExistingDestinationIsNeverOverwritten()
        {
            Texture2D source = CreateTexture(_sourceFolder, "Collision.asset");
            Texture2D destination = CreateTexture(
                _destinationFolder, "Collision.asset");
            GlobalObjectId destinationObjectId =
                GlobalObjectId.GetGlobalObjectIdSlow(destination);
            string destinationGuid = AssetDatabase.AssetPathToGUID(
                _destinationFolder + "/Collision.asset");

            List<UmaMoveUnusedTextureResult> results =
                UmaMoveUnusedTexturesUtility.ProcessTextures(
                    new[] { source }, _destinationFolder,
                    Array.Empty<OverlayDataAsset>());

            Assert.AreEqual(UmaMoveUnusedTextureStatus.Error,
                results[0].Status);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(
                _sourceFolder + "/Collision.asset"));
            Texture2D persistedDestination =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    _destinationFolder + "/Collision.asset");
            Assert.IsNotNull(persistedDestination);
            Assert.AreEqual(destinationObjectId,
                GlobalObjectId.GetGlobalObjectIdSlow(persistedDestination));
            Assert.AreEqual(destinationGuid, AssetDatabase.AssetPathToGUID(
                _destinationFolder + "/Collision.asset"));
            Assert.That(results[0].Details,
                Has.Some.Contains("never overwritten"));
        }

        [Test]
        public void MissingOverlayAbortsBeforeAnyTextureIsMoved()
        {
            Texture2D texture = CreateTexture(_sourceFolder, "Protected.asset");

            List<UmaMoveUnusedTextureResult> results =
                UmaMoveUnusedTexturesUtility.ProcessTextures(
                    new[] { texture }, _destinationFolder,
                    new OverlayDataAsset[] { null });

            Assert.AreEqual(UmaMoveUnusedTextureStatus.Error,
                results[0].Status);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(
                _sourceFolder + "/Protected.asset"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Texture2D>(
                _destinationFolder + "/Protected.asset"));
            Assert.That(results[0].Details,
                Has.Some.Contains("No textures were moved"));
        }

        private Texture2D CreateTexture(string folder, string fileName)
        {
            var texture = new Texture2D(2, 2)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(fileName)
            };
            AssetDatabase.CreateAsset(texture, folder + "/" + fileName);
            return texture;
        }

        private OverlayDataAsset CreateOverlay(string fileName)
        {
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            overlay.name = System.IO.Path.GetFileNameWithoutExtension(fileName);
            AssetDatabase.CreateAsset(overlay, _rootFolder + "/" + fileName);
            return overlay;
        }
    }
}
#endif
