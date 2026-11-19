using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// The Project window marker shows a Unity-serialized asset with changes not yet written to disk,
    /// and leaves alone imported files (which can report changes Unity never writes) and folders.
    /// Works in its own temporary folder and saves only its own asset.
    /// </summary>
    public class UMAUnsavedAssetMarkerTests
    {
        private string folder;

        [SetUp]
        public void SetUp()
        {
            string name = "__UMAUnsavedAssetMarkerTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            folder = "Assets/" + name;
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void SerializedAssetIsMarkedUntilSaved()
        {
            var dna = ScriptableObject.CreateInstance<DNA>();
            string path = folder + "/MarkerTest.asset";
            AssetDatabase.CreateAsset(dna, path);
            Assert.That(UMAUnsavedAssetMarker.IsUnsaved(path), Is.False, "Just written to disk.");

            dna.defaultValue = 0.25f;
            EditorUtility.SetDirty(dna);
            Assert.That(UMAUnsavedAssetMarker.IsUnsaved(path), Is.True, "Changed in memory only.");

            AssetDatabase.SaveAssetIfDirty(dna);
            Assert.That(UMAUnsavedAssetMarker.IsUnsaved(path), Is.False, "Saved.");
        }

        [Test]
        public void ImportedFilesAndFoldersAreNotMarked()
        {
            var source = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            string path = folder + "/MarkerTest.png";
            File.WriteAllBytes(path, source.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(texture, Is.Not.Null);

            // Building a character marks the textures it reads dirty, but Unity never writes an imported texture.
            EditorUtility.SetDirty(texture);
            Assert.That(EditorUtility.IsDirty(texture), Is.True);
            Assert.That(UMAUnsavedAssetMarker.IsUnsaved(path), Is.False);
            Assert.That(UMAUnsavedAssetMarker.IsUnsaved(folder), Is.False);
            Assert.That(UMAUnsavedAssetMarker.IsUnsaved(null), Is.False);
        }
    }
}
