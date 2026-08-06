#if UNITY_INCLUDE_TESTS
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintDocumentPersistenceTests
    {
        private const string Folder = "Assets/UMA/OverlayPainter/GeneratedPersistenceTests";
        private string recoveryKey;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets/UMA/OverlayPainter", "GeneratedPersistenceTests");
            TexturePaintRecoveryStore.RecoveryFolderOverride = Folder + "/Recovery";
            recoveryKey = "test-" + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                TexturePaintRecoveryStore.Delete(recoveryKey);
                AssetDatabase.DeleteAsset(Folder);
            }
            finally { TexturePaintRecoveryStore.RecoveryFolderOverride = null; }
        }

        [Test]
        public void TransientDocumentIsNotAProjectAsset()
        {
            TexturePaintDocument document = TexturePaintDocumentStorage.CreateTransient(null);
            try
            {
                Assert.That(AssetDatabase.GetAssetPath(document), Is.Empty);
                Assert.That(document.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
                Assert.That(document.createdUtc, Is.Not.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }

        [Test]
        public void RecoveryJournalRoundTripsExternalPixelBlob()
        {
            TexturePaintDocument source = CreateDocumentWithPixels(new byte[] { 4, 8, 15, 16, 23, 42 });
            try
            {
                TexturePaintRecoveryStore.SaveImmediate(source, recoveryKey);
                Assert.That(TexturePaintRecoveryStore.HasRecovery(recoveryKey), Is.True);
                Assert.That(TexturePaintRecoveryStore.RecoveryAssetPath,
                    Is.EqualTo(Folder + "/Recovery/painter_recovery.asset"));
                TexturePaintDocument recoveryAsset = AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(
                    TexturePaintRecoveryStore.RecoveryAssetPath);
                Assert.That(recoveryAsset, Is.Not.Null);
                Assert.That(recoveryAsset.recoverySnapshot, Is.True);
                Assert.That(recoveryAsset.recoveryContextKey, Is.EqualTo(recoveryKey));
                TexturePaintPixelData storedPixels = recoveryAsset.surfaces[0].baseChannels[0].pixels;
                Assert.That(storedPixels.compressedBytes, Is.Null.Or.Empty);
                Assert.That(storedPixels.dataAsset, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(storedPixels.dataAsset),
                    Does.StartWith(Folder + "/Recovery/painter_recovery Data/"));
                TexturePaintRecoveryStore.SaveImmediate(source, recoveryKey);
                Assert.That(AssetDatabase.FindAssets("t:TextAsset",
                    new[] { TexturePaintRecoveryStore.RecoveryDataFolder }).Length, Is.EqualTo(1),
                    "Saving an unchanged recovery should reuse its content-addressed data asset.");
                Assert.That(TexturePaintRecoveryStore.TryLoad(recoveryKey, out TexturePaintDocument restored,
                    out string error), Is.True, error);
                try
                {
                    TexturePaintPixelData pixels = restored.surfaces[0].baseChannels[0].pixels;
                    Assert.That(pixels.compressedBytes, Is.EqualTo(new byte[] { 4, 8, 15, 16, 23, 42 }));
                    Assert.That(pixels.recoveryBlobKey, Is.Null.Or.Empty);
                    Assert.That(AssetDatabase.GetAssetPath(restored), Is.Empty);
                }
                finally { UnityEngine.Object.DestroyImmediate(restored); }
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void RecoveryAssetIsOnlyOfferedAndDeletedForItsContext()
        {
            TexturePaintDocument source = CreateDocumentWithPixels(new byte[] { 2, 4, 6, 8 });
            try
            {
                TexturePaintRecoveryStore.SaveImmediate(source, recoveryKey);
                string otherKey = "other-" + Guid.NewGuid().ToString("N");
                Assert.That(TexturePaintRecoveryStore.HasRecovery(otherKey), Is.False);
                TexturePaintRecoveryStore.Delete(otherKey);
                Assert.That(AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(
                    TexturePaintRecoveryStore.RecoveryAssetPath), Is.Not.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void ProjectSaveExternalizesPixelsAndSaveAsOwnsIndependentBlob()
        {
            TexturePaintDocument source = CreateDocumentWithPixels(new byte[] { 1, 3, 3, 7 });
            TexturePaintDocument copiedSnapshot = null;
            try
            {
                string firstPath = Folder + "/First.asset";
                using (TexturePaintProjectSaveOperation first =
                    new TexturePaintProjectSaveOperation(source, null, firstPath))
                {
                    Complete(first);
                    Assert.That(first.HasError, Is.False, first.Error);
                    Assert.That(first.SavedDocument, Is.Not.Null);
                    TexturePaintPixelData firstPixels = first.SavedDocument.surfaces[0].baseChannels[0].pixels;
                    Assert.That(firstPixels.compressedBytes, Is.Null.Or.Empty);
                    Assert.That(firstPixels.dataAsset, Is.Not.Null);
                    string firstBlobPath = AssetDatabase.GetAssetPath(firstPixels.dataAsset);
                    Assert.That(firstBlobPath, Does.StartWith(Folder + "/First Data/"));

                    copiedSnapshot = UnityEngine.Object.Instantiate(first.SavedDocument);
                    copiedSnapshot.hideFlags = HideFlags.HideAndDontSave;
                    string secondPath = Folder + "/Second.asset";
                    using TexturePaintProjectSaveOperation second =
                        new TexturePaintProjectSaveOperation(copiedSnapshot, null, secondPath);
                    Complete(second);
                    Assert.That(second.HasError, Is.False, second.Error);
                    TexturePaintPixelData secondPixels = second.SavedDocument.surfaces[0].baseChannels[0].pixels;
                    string secondBlobPath = AssetDatabase.GetAssetPath(secondPixels.dataAsset);
                    Assert.That(secondBlobPath, Does.StartWith(Folder + "/Second Data/"));
                    Assert.That(secondBlobPath, Is.Not.EqualTo(firstBlobPath));
                    Assert.That(secondPixels.dataAsset.bytes, Is.EqualTo(firstPixels.dataAsset.bytes));
                }
            }
            finally
            {
                if (copiedSnapshot != null) UnityEngine.Object.DestroyImmediate(copiedSnapshot);
                if (source != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source)))
                    UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CompletedCaptureTransitionsToOneRecoveryWriterAndCompletes()
        {
            TexturePaintDocument source = TexturePaintDocumentStorage.CreateTransient(null);
            TextureStore store = new TextureStore();
            TexturePaintStageController controller = new TexturePaintStageController();
            TexturePaintStageWindow stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            string key = "transition-" + Guid.NewGuid().ToString("N");
            try
            {
                TexturePaintDocumentStorage.CaptureOperation capture = TexturePaintDocumentStorage.BeginCapture(
                    source, store, new TexturePaintMaskStack(),
                    new System.Collections.Generic.Dictionary<EditableTextureTarget, long>(), true);
                Assert.That(capture.IsDone, Is.True);
                SetField(stage, "controller", controller);
                SetField(stage, "document", source);
                SetField(stage, "recoveryContextKey", key);
                SetField(stage, "persistenceCapture", capture);
                FieldInfo intentField = Field("persistenceIntent");
                intentField.SetValue(stage, Enum.Parse(intentField.FieldType, "Recovery"));

                Invoke(stage, "PersistenceUpdate");
                TexturePaintRecoveryWriteOperation firstWriter =
                    (TexturePaintRecoveryWriteOperation)Field("recoveryWrite").GetValue(stage);
                Assert.That(firstWriter, Is.Not.Null);

                Invoke(stage, "PersistenceUpdate");
                Assert.That(Field("recoveryWrite").GetValue(stage), Is.SameAs(firstWriter),
                    "A completed capture must not recreate its commit writer every editor update.");

                firstWriter.CompleteSynchronously();
                Invoke(stage, "PersistenceUpdate");
                Assert.That(Field("persistenceCapture").GetValue(stage), Is.Null);
                Assert.That(Field("recoveryWrite").GetValue(stage), Is.Null);
            }
            finally
            {
                TexturePaintRecoveryStore.Delete(key);
                TexturePaintDocument current = Field("document").GetValue(stage) as TexturePaintDocument;
                if (current != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(current)))
                    UnityEngine.Object.DestroyImmediate(current);
                if (source != null && source != current && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source)))
                    UnityEngine.Object.DestroyImmediate(source);
                controller.Dispose();
                store.Dispose();
                UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        private static TexturePaintDocument CreateDocumentWithPixels(byte[] bytes)
        {
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.name = "Persistence Test";
            document.createdUtc = DateTime.UtcNow.ToString("O");
            document.surfaces.Add(new TexturePaintDocumentSurface
            {
                stableId = "surface",
                baseChannels = new System.Collections.Generic.List<TexturePaintDocumentChannel>
                {
                    new TexturePaintDocumentChannel
                    {
                        channel = TexturePaintChannel.Albedo,
                        pixels = new TexturePaintPixelData
                        {
                            width = 1,
                            height = 1,
                            uncompressedByteCount = 4,
                            storageKey = "surface/base/Albedo",
                            compressedBytes = bytes
                        }
                    }
                }
            });
            return document;
        }

        private static void Complete(TexturePaintProjectSaveOperation operation)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (!operation.IsDone && timeout.Elapsed < TimeSpan.FromSeconds(10))
            {
                operation.Tick();
                if (!operation.IsDone) Thread.Sleep(5);
            }
            Assert.That(operation.IsDone, Is.True, "Timed out waiting for the project document save operation.");
        }

        private static FieldInfo Field(string name)
        {
            FieldInfo field = typeof(TexturePaintStageWindow).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing TexturePaintStageWindow field: " + name);
            return field;
        }

        private static void SetField(TexturePaintStageWindow stage, string name, object value)
        {
            Field(name).SetValue(stage, value);
        }

        private static void Invoke(TexturePaintStageWindow stage, string name)
        {
            MethodInfo method = typeof(TexturePaintStageWindow).GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Missing TexturePaintStageWindow method: " + name);
            method.Invoke(stage, null);
        }
    }
}
#endif
