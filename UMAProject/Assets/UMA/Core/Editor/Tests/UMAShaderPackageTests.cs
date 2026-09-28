using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class UMAShaderPackageTests
    {
        private string folder;
        private Shader sourceShader, targetShader;
        private Material source;
        private UMAMaterial owner, other;

        [Serializable] private sealed class Pack { public Entry[] entries; }
        [Serializable] private sealed class Entry { public int srpTarget; public int UnityVersionMin; public int UnityVersionMax = 30000; public string shaderSrc; }

        [SetUp]
        public void SetUp()
        {
            string id = Guid.NewGuid().ToString("N");
            folder = "Assets/UMA_ShaderPackageTests_" + id;
            AssetDatabase.CreateFolder("Assets", "UMA_ShaderPackageTests_" + id);
            string src = ShaderSource("Hidden/UMA/PackageTest/" + id, "_OldTex");
            var pack = new Pack { entries = Enumerable.Range(0, 3).Select(i => new Entry { srpTarget = i, UnityVersionMin = 0, shaderSrc = src }).ToArray() };
            string packagePath = folder + "/Source.umaShaderPack";
            File.WriteAllText(packagePath, "{\"MonoBehaviour\":" + JsonUtility.ToJson(pack) + "}");
            AssetDatabase.ImportAsset(packagePath, ImportAssetOptions.ForceSynchronousImport);
            sourceShader = AssetDatabase.LoadAssetAtPath<Shader>(packagePath);
            Assert.NotNull(sourceShader, "Test package must import as a Shader.");
            string shaderPath = folder + "/Replacement.shader";
            File.WriteAllText(shaderPath, ShaderSource("Hidden/UMA/ReplacementTest/" + id, "_NewTex"));
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            targetShader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Assert.NotNull(targetShader);
            source = new Material(sourceShader) { name = "Shared source" };
            source.SetTexture("_OldTex", Texture2D.whiteTexture);
            source.SetTextureScale("_OldTex", new Vector2(2, 3));
            source.SetTextureOffset("_OldTex", new Vector2(.1f, .2f));
            source.SetColor("_Tint", Color.red);
            AssetDatabase.CreateAsset(source, folder + "/Source.mat");
            owner = MakeOwner("Owner");
            other = MakeOwner("Other");
        }

        private static string ShaderSource(string name, string property) =>
            "Shader \"" + name + "\" { Properties { " + property + " (\"Texture\", 2D) = \"white\" {} _Tint (\"Tint\", Color) = (1,1,1,1) } SubShader { Pass { } } }";

        private UMAMaterial MakeOwner(string name)
        {
            var value = ScriptableObject.CreateInstance<UMAMaterial>();
            value.name = name;
            value.material = source;
            value.channels = new[] { new UMAMaterial.MaterialChannel { materialPropertyName = "_OldTex", channelType = UMAMaterial.ChannelType.DiffuseTexture } };
            AssetDatabase.CreateAsset(value, folder + "/" + name + ".asset");
            return value;
        }

        private UMAShaderPackageUtility.Replacement Request()
        {
            var request = new UMAShaderPackageUtility.Replacement
            {
                Owner = owner, SourceShader = sourceShader, Shader = targetShader, OwnerSnapshot = EditorJsonUtility.ToJson(owner)
            };
            request.Slots.Add(0);
            request.Channels.Add(new UMAShaderPackageUtility.ChannelMapping { Property = "_NewTex" });
            return request;
        }

        private void Slot(UMAMaterial value, int slot, Material material)
        {
            using var serialized = new SerializedObject(value);
            serialized.FindProperty(UMAShaderPackageUtility.MaterialSlots[slot]).objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            if (owner != null) Undo.ClearUndo(owner);
            if (other != null) Undo.ClearUndo(other);
            if (source != null) Undo.ClearUndo(source);
            if (!string.IsNullOrEmpty(folder)) AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void ScanFindsAllFourSlotsAndDeduplicatesOwnersAndMaterials()
        {
            for (int i = 1; i < 4; i++) Slot(owner, i, source);
            var row = UMAShaderPackageUtility.Scan(new[] { folder }).Single();
            Assert.AreEqual(sourceShader, row.Shader);
            Assert.AreEqual(1, row.Materials.Count);
            Assert.AreEqual(2, row.Uses.Count);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, row.Uses.Single(u => u.Owner == owner).Slots);
            Assert.IsTrue(UMAShaderPackageUtility.IsPackage("Assets/Test.UMASHADERPACK"));
        }

        [Test]
        public void ReplaceCopiesMaterialAndRemapsTextureWithoutChangingOtherUsers()
        {
            string sourceBefore = EditorJsonUtility.ToJson(source);
            string otherBefore = EditorJsonUtility.ToJson(other);
            var copies = UMAShaderPackageUtility.Replace(Request(), folder);
            Assert.AreEqual(1, copies.Count);
            Assert.AreNotSame(source, owner.material);
            Assert.AreSame(targetShader, owner.material.shader);
            Assert.AreSame(Texture2D.whiteTexture, owner.material.GetTexture("_NewTex"));
            Assert.AreEqual(new Vector2(2, 3), owner.material.GetTextureScale("_NewTex"));
            Assert.AreEqual(new Vector2(.1f, .2f), owner.material.GetTextureOffset("_NewTex"));
            Assert.AreEqual(Color.red, owner.material.GetColor("_Tint"));
            Assert.AreEqual("_NewTex", owner.channels[0].materialPropertyName);
            Assert.AreEqual(targetShader.name, owner.ShaderName);
            Assert.AreEqual(sourceBefore, EditorJsonUtility.ToJson(source));
            Assert.AreEqual(otherBefore, EditorJsonUtility.ToJson(other));
            var row = UMAShaderPackageUtility.Scan(new[] { folder }).Single();
            Assert.AreEqual(other, row.Uses.Single().Owner);
            Assert.AreEqual(source, row.Materials.Single());
        }

        [Test]
        public void MultipleMatchingSlotsShareOneNewCopyInsideSelectedOwner()
        {
            Slot(owner, 1, source); Slot(owner, 2, source); Slot(owner, 3, source);
            var request = Request(); request.Slots.AddRange(new[] { 1, 2, 3 });
            var copies = UMAShaderPackageUtility.Replace(request, folder);
            Assert.AreEqual(1, copies.Count);
            for (int i = 0; i < 4; i++) Assert.AreSame(copies[0], UMAShaderPackageUtility.GetMaterial(owner, i));
            Assert.AreSame(source, other.material);
        }

        [Test]
        public void InvalidOrStaleMappingsCannotCreateCopies()
        {
            var request = Request();
            request.Shader = sourceShader;
            StringAssert.Contains("not imported", UMAShaderPackageUtility.Validate(request));
            request.Shader = targetShader; request.Channels[0].Property = "_Missing";
            int before = AssetDatabase.FindAssets("t:Material", new[] { folder }).Length;
            Assert.Throws<InvalidOperationException>(() => UMAShaderPackageUtility.Replace(request, folder));
            Assert.AreEqual(before, AssetDatabase.FindAssets("t:Material", new[] { folder }).Length);
            request.Channels[0].Property = "_NewTex";
            owner.channels[0].DownSample = 4;
            StringAssert.Contains("changed", UMAShaderPackageUtility.Validate(request));
            Assert.AreSame(source, owner.material);
        }

        [Test]
        public void UnchangedPassBlocksIncompatibleSharedChannelMapping()
        {
            Slot(owner, 1, source);
            var request = Request();
            StringAssert.Contains("Second pass", UMAShaderPackageUtility.Validate(request));
            request.Channels[0].Unused = true;
            StringAssert.Contains("still used", UMAShaderPackageUtility.Validate(request));
            request.Slots.Add(1);
            Assert.IsNull(UMAShaderPackageUtility.Validate(request));
        }

        [Test]
        public void UndoAndRedoRestoreReferencesAndMappingsWhileKeepingCopies()
        {
            var copy = UMAShaderPackageUtility.Replace(Request(), folder).Single();
            string copyPath = AssetDatabase.GetAssetPath(copy);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.AreSame(source, owner.material);
            Assert.AreEqual("_OldTex", owner.channels[0].materialPropertyName);
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<Material>(copyPath));
            Undo.PerformRedo();
            Assert.AreSame(copy, owner.material);
            Assert.AreEqual("_NewTex", owner.channels[0].materialPropertyName);
        }

        [Test]
        public void UpdateConsolidatesSharedChannelsAndUndoRestoresEveryOwner()
        {
            other.channels = new[]
            {
                other.channels[0],
                new UMAMaterial.MaterialChannel { materialPropertyName = "_Tint", channelType = UMAMaterial.ChannelType.MaterialColor }
            };
            Slot(other, 3, source);
            var request = Request();
            UMAShaderPackageUtility.RefreshShared(request);
            var shared = request.Shared.Single();
            Assert.AreSame(other, shared.Owner);
            Assert.AreSame(request.Channels[0], shared.Channels[0]);
            Assert.AreEqual("_Tint", shared.Channels[1].Property);
            int before = AssetDatabase.FindAssets("t:Material", new[] { folder }).Length;
            var updated = UMAShaderPackageUtility.Update(request);
            Assert.AreSame(source, updated.Single());
            Assert.AreSame(source, owner.material);
            Assert.AreSame(source, other.material);
            Assert.AreSame(targetShader, source.shader);
            Assert.AreEqual("_NewTex", owner.channels[0].materialPropertyName);
            Assert.AreEqual("_NewTex", other.channels[0].materialPropertyName);
            Assert.AreEqual(targetShader.name, other.ShaderName);
            Assert.AreEqual(before, AssetDatabase.FindAssets("t:Material", new[] { folder }).Length);
            Assert.AreEqual(new Vector2(2, 3), source.GetTextureScale("_NewTex"));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.AreSame(sourceShader, source.shader);
            Assert.AreEqual("_OldTex", owner.channels[0].materialPropertyName);
            Assert.AreEqual("_OldTex", other.channels[0].materialPropertyName);
            Undo.PerformRedo();
            Assert.AreSame(targetShader, source.shader);
            Assert.AreEqual("_NewTex", other.channels[0].materialPropertyName);
        }

        [Test]
        public void UpdateRejectsNewSharedUsersBeforeChangingAnyAssets()
        {
            var request = Request();
            UMAShaderPackageUtility.RefreshShared(request);
            var added = MakeOwner("New user");
            Assert.Throws<InvalidOperationException>(() => UMAShaderPackageUtility.Update(request));
            Assert.AreSame(sourceShader, source.shader);
            Assert.AreEqual("_OldTex", owner.channels[0].materialPropertyName);
            Assert.AreEqual("_OldTex", added.channels[0].materialPropertyName);
        }

        [Test]
        public void UpdateRejectsIncompatibleRemainingPassOnSharedOwner()
        {
            var separate = new Material(sourceShader);
            AssetDatabase.CreateAsset(separate, folder + "/Separate.mat");
            Slot(other, 1, separate);
            var request = Request();
            UMAShaderPackageUtility.RefreshShared(request);
            StringAssert.Contains("Second pass", UMAShaderPackageUtility.ValidateUpdate(request));
            Assert.Throws<InvalidOperationException>(() => UMAShaderPackageUtility.Update(request));
            Assert.AreSame(sourceShader, source.shader);
            Assert.AreEqual("_OldTex", other.channels[0].materialPropertyName);
        }

        private static byte[] ZipBytes(ZipArchive archive, string name)
        {
            using var stream = archive.GetEntry(name).Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return bytes.ToArray();
        }

        [Test]
        public void ArchivePreservesPackageAndMetaBytesBeforeDeletingSource()
        {
            string path = AssetDatabase.GetAssetPath(sourceShader);
            byte[] package = File.ReadAllBytes(path), meta = File.ReadAllBytes(path + ".meta");
            string zipPath = folder + "/Archive.zip";
            string entry = UMAShaderPackageArchive.Archive(path, zipPath);
            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(File.Exists(path + ".meta"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Shader>(path));
            using var stream = File.OpenRead(zipPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            CollectionAssert.AreEqual(package, ZipBytes(archive, entry));
            CollectionAssert.AreEqual(meta, ZipBytes(archive, entry + ".meta"));
        }

        [Test]
        public void ArchivePreservesExistingEntriesAndStoresNameCollisionsSeparately()
        {
            string path = AssetDatabase.GetAssetPath(sourceShader);
            byte[] package = File.ReadAllBytes(path), meta = File.ReadAllBytes(path + ".meta");
            string zipPath = folder + "/Archive.zip";
            using (var stream = File.Create(zipPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            using (var entry = archive.CreateEntry(Path.GetFileName(path)).Open())
                entry.Write(new byte[] { 1, 2, 3 }, 0, 3);
            AssetDatabase.ImportAsset(zipPath, ImportAssetOptions.ForceSynchronousImport);
            string newEntry = UMAShaderPackageArchive.Archive(path, zipPath);
            StringAssert.StartsWith("Snapshots/", newEntry);
            using var saved = File.OpenRead(zipPath);
            using var zip = new ZipArchive(saved, ZipArchiveMode.Read);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ZipBytes(zip, Path.GetFileName(path)));
            CollectionAssert.AreEqual(package, ZipBytes(zip, newEntry));
            CollectionAssert.AreEqual(meta, ZipBytes(zip, newEntry + ".meta"));
            Assert.AreEqual(3, zip.Entries.Count);
        }

        [Test]
        public void ArchiveFailureLeavesSourceAndExistingZipUnchanged()
        {
            string path = AssetDatabase.GetAssetPath(sourceShader);
            byte[] package = File.ReadAllBytes(path), meta = File.ReadAllBytes(path + ".meta");
            string zipPath = folder + "/Archive.zip";
            byte[] invalidZip = { 1, 2, 3, 4 };
            File.WriteAllBytes(zipPath, invalidZip);
            AssetDatabase.ImportAsset(zipPath, ImportAssetOptions.ForceSynchronousImport);
            Assert.Throws<InvalidDataException>(() => UMAShaderPackageArchive.Archive(path, zipPath));
            CollectionAssert.AreEqual(package, File.ReadAllBytes(path));
            CollectionAssert.AreEqual(meta, File.ReadAllBytes(path + ".meta"));
            CollectionAssert.AreEqual(invalidZip, File.ReadAllBytes(zipPath));
            Assert.IsEmpty(Directory.GetFiles(folder, ".uma-archive-*.tmp"));
        }

        [Test]
        public void WindowLayoutLoadsAndContainsBothPanes()
        {
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UMAEditorUtilities.FindUMAFullPath() + "/Core/Editor/Scripts/UMAShaderPackageWindow.uxml");
            Assert.NotNull(layout);
            var root = layout.CloneTree();
            Assert.NotNull(root.Q("packages"));
            Assert.NotNull(root.Q<ScrollView>("details"));
            Assert.NotNull(root.Q<TwoPaneSplitView>());
        }
    }
}
