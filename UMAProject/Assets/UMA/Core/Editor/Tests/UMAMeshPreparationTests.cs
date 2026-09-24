using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Tests
{
    public class UMAMeshPreparationTests
    {
        internal sealed class GeneratorScope : IDisposable
        {
            private readonly UMAGenerator previous = UMAAssetIndexer.Instance.generator;
            private readonly GameObject temporary = new GameObject("Slot preparation test generator");
            internal GeneratorScope() => UMAAssetIndexer.Instance.generator = temporary.AddComponent<UMAGenerator>();
            public void Dispose()
            {
                UMAAssetIndexer.Instance.generator = previous;
                UnityEngine.Object.DestroyImmediate(temporary);
            }
        }
        internal static UMAMeshData Mesh(int count = 12)
        {
            int root = UMAUtils.StringToHash("Global");
            var m = new UMAMeshData {
                SlotName = "Prepared", vertexCount = count, subMeshCount = 1,
                vertices = Enumerable.Range(0, count).Select(i => new Vector3(i % 3, i / 3, .1f)).ToArray(),
                normals = Enumerable.Repeat(Vector3.forward, count).ToArray(),
                tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), count).ToArray(),
                uv = Enumerable.Range(0, count).Select(i => new Vector2(i % 3, i / 3)).ToArray(),
                submeshes = new[] { new SubMeshTriangles(Enumerable.Range(0, count - count % 3).ToArray()) },
                bindPoses = new[] { Matrix4x4.identity }, boneNameHashes = new[] { root },
                umaBones = new[] { new UMATransform { name = "Global", hash = root, rotation = Quaternion.identity, scale = Vector3.one } },
                umaBoneCount = 1, rootBoneHash = root, RootBoneName = "Global",
                ManagedBonesPerVertex = Enumerable.Repeat((byte)1, count).ToArray(),
                ManagedBoneWeights = Enumerable.Repeat(new BoneWeight1 { boneIndex = 0, weight = 1 }, count).ToArray(),
                blendShapes = new[] { new UMABlendShape { shapeName = "TestShape", frames = new[] {
                    new UMABlendFrame(count) { frameWeight = 50 }, new UMABlendFrame(count) { frameWeight = 100 } } } }
            };
            m.blendShapes[0].frames[0].deltaVertices[0] = Vector3.up * .2f;
            m.blendShapes[0].frames[0].deltaNormals[1] = Vector3.right * .2f;
            m.blendShapes[0].frames[0].deltaTangents[2] = Vector3.up * .2f;
            m.blendShapes[0].frames[1].deltaVertices[3] = Vector3.up * .4f;
            return m;
        }

        [Test]
        public void ZeroDetectionIsExactAndRejectsNonfiniteValues()
        {
            Assert.IsTrue(UMABlendFrame.isAllZero(null));
            Assert.IsTrue(UMABlendFrame.isAllZero(Array.Empty<Vector3>()));
            Assert.IsTrue(UMABlendFrame.isAllZero(new Vector3[5]));
            Assert.IsFalse(UMABlendFrame.isAllZero(new[] { new Vector3(float.Epsilon, 0, 0) }));
            Assert.IsFalse(UMABlendFrame.isAllZero(new[] { new Vector3(float.NaN, 0, 0) }));
            Assert.IsFalse(UMABlendFrame.isAllZero(new[] { new Vector3(0, float.PositiveInfinity, 0) }));
        }

        [Test]
        public void PreparationStripsOnlyZeroOptionalChannelsAndRoundTrips()
        {
            var m = Mesh(); var vertices = (Vector3[])m.vertices.Clone();
            m.PrepareBuildData();
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out var p));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, p.BlendShapes[0].Frames[0].AffectedVertices);
            CollectionAssert.AreEqual(new[] { 3 }, p.BlendShapes[0].Frames[1].AffectedVertices);
            Assert.IsEmpty(m.blendShapes[0].frames[1].deltaNormals);
            Assert.AreEqual(12, m.blendShapes[0].frames[0].deltaNormals.Length);
            Assert.AreEqual(12, m.blendShapes[0].frames[1].deltaVertices.Length);
            CollectionAssert.AreEqual(vertices, m.vertices);
            var copy = JsonUtility.FromJson<UMAMeshData>(JsonUtility.ToJson(m));
            Assert.IsTrue(UMAMeshPreparation.TryGet(copy, out var reloaded), "Serialized metadata must survive null-to-empty normalization.");
            Assert.AreEqual(p.SourceFingerprint, reloaded.SourceFingerprint);
            Assert.AreSame(copy.PreparedData, reloaded);
        }

        [Test]
        public void NullableSerializedSourceFieldsDoNotRequireAnotherConversion()
        {
            var m = Mesh(); m.RootBoneName = null; m.umaBones[0].name = null;
            m.PrepareBuildData();
            var copy = JsonUtility.FromJson<UMAMeshData>(JsonUtility.ToJson(m));
            var saved = copy.PreparedData;
            Assert.IsTrue(UMAMeshPreparation.TryGet(copy, out var verified));
            Assert.AreSame(saved, verified);
        }

        [Test]
        public void InvalidLegacyWeightsDoNotPreventSavingAnOldSlot()
        {
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>(); slot.meshData = Mesh();
            try
            {
                slot.meshData.ManagedBonesPerVertex = null; slot.meshData.boneWeights = null;
                Assert.IsFalse(UMA.Editors.UMASlotPreparationUtility.PrepareSlot(slot));
                Assert.AreEqual(12, slot.meshData.blendShapes[0].frames[1].deltaNormals.Length);
                Assert.IsNull(slot.meshData.PreparedData);
            }
            finally { UnityEngine.Object.DestroyImmediate(slot); }
        }

        [TestCase("positions")] [TestCase("normals")] [TestCase("tangents")]
        [TestCase("uv")] [TestCase("weights")] [TestCase("bindposes")]
        [TestCase("hierarchy")] [TestCase("shape")] [TestCase("shapename")]
        [TestCase("topology")] [TestCase("lod")] [TestCase("cloth")]
        [TestCase("colors")] [TestCase("uv2")] [TestCase("uv3")] [TestCase("uv4")]
        [TestCase("bonehash")] [TestCase("legacyweights")] [TestCase("rootname")]
        [TestCase("frameweight")] [TestCase("shapenormal")] [TestCase("shapetangent")]
        public void SourceEditsInvalidatePreparationAndChangeFingerprint(string field)
        {
            var m = Mesh(); m.PrepareBuildData();
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out var old));
            switch (field)
            {
                case "positions": m.vertices[0] += Vector3.right; break;
                case "normals": m.normals[0] = Vector3.up; break;
                case "tangents": m.tangents[0].w = -1; break;
                case "uv": m.uv[0] = Vector2.one; break;
                case "colors": m.colors32 = Enumerable.Repeat(new Color32(1, 2, 3, 4), 12).ToArray(); break;
                case "uv2": m.uv2 = Enumerable.Repeat(Vector2.one, 12).ToArray(); break;
                case "uv3": m.uv3 = Enumerable.Repeat(Vector2.one, 12).ToArray(); break;
                case "uv4": m.uv4 = Enumerable.Repeat(Vector2.one, 12).ToArray(); break;
                case "bonehash": m.boneNameHashes[0]++; break;
                case "legacyweights": m.boneWeights = new UMABoneWeight[12]; break;
                case "rootname": m.RootBoneName = "RenamedRoot"; break;
                case "weights": m.ManagedBoneWeights[0].weight = .5f; break;
                case "bindposes": m.bindPoses[0] = Matrix4x4.Translate(Vector3.up); break;
                case "hierarchy": m.umaBones[0].position = Vector3.one; break;
                case "shape": m.blendShapes[0].frames[1].deltaVertices[7] = Vector3.forward; break;
                case "shapename": m.blendShapes[0].shapeName = "Renamed"; break;
                case "frameweight": m.blendShapes[0].frames[0].frameWeight = 40; break;
                case "shapenormal": m.blendShapes[0].frames[0].deltaNormals[7] = Vector3.forward; break;
                case "shapetangent": m.blendShapes[0].frames[0].deltaTangents[7] = Vector3.forward; break;
                case "topology": m.submeshes[0].getManagedTriangles(-1)[0] = 1; break;
                case "lod": m.submeshes[0].lodRanges = new List<UMALodRange> { new UMALodRange(0, 12), new UMALodRange(3, 3) }; break;
                case "cloth": m.clothSkinningSerialized = Enumerable.Repeat(Vector2.one, 12).ToArray(); break;
            }
            m.InvalidateBuildData();
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out var refreshed));
            Assert.AreNotEqual(old.SourceFingerprint, refreshed.SourceFingerprint);
            Assert.AreSame(refreshed, m.PreparedData, "A later save must persist the refreshed format.");
            m.PrepareBuildData();
            Assert.AreEqual(refreshed.SourceFingerprint, m.PreparedData.SourceFingerprint);
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out _));
        }

        [Test]
        public void MissingStaleSchemaAndDamagedMetadataConvertAutomatically()
        {
            var m = Mesh();
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out var transient));
            Assert.AreSame(transient, m.PreparedData);
            Assert.AreEqual(12, m.blendShapes[0].frames[1].deltaNormals.Length, "Loading must not mutate shared channels.");
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out var again));
            Assert.AreSame(transient, again, "Convert once, not once per avatar.");
            m.PrepareBuildData();
            typeof(UMAMeshPreparation).GetField("version", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(m.PreparedData, -1);
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out var upgraded));
            Assert.AreEqual(UMAMeshPreparation.CurrentVersion, upgraded.Version);
            Assert.AreSame(upgraded, m.PreparedData);
            m.PrepareBuildData();
            m.PreparedData.BlendShapes[0].Frames[0].AffectedVertices[0] = 900;
            m.InvalidateBuildData();
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out var repaired));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, repaired.BlendShapes[0].Frames[0].AffectedVertices);
            m.PreparedData.BlendShapes[0].Frames = null;
            m.InvalidateBuildData();
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out _), "Null damaged metadata must also recover.");
            m.PrepareBuildData();
            // Simulate a stale asset saved by an external tool without updating metadata.
            m.vertices[0].x = 100;
            var copy = JsonUtility.FromJson<UMAMeshData>(JsonUtility.ToJson(m));
            var stale = copy.PreparedData.SourceFingerprint;
            Assert.IsTrue(UMAMeshPreparation.TryGet(copy, out var changed));
            Assert.AreNotEqual(stale, changed.SourceFingerprint);
            Assert.AreSame(changed, copy.PreparedData);
        }

        [Test]
        public void InvalidUpgradeDoesNotStripSourceChannels()
        {
            var m = Mesh(); m.ManagedBoneWeights[0].boneIndex = 50;
            Assert.Throws<InvalidOperationException>(() => m.PrepareBuildData());
            Assert.AreEqual(12, m.blendShapes[0].frames[1].deltaNormals.Length);
            Assert.IsNull(m.PreparedData);
            Assert.IsFalse(UMAMeshPreparation.TryGet(m, out _), "Invalid source data keeps the original validation path.");
        }

        [Test]
        public void ExtractionPreservesTinyDeltasAndStripsOnlyZeroNormalTangentChannels()
        {
            var mesh = new UnityEngine.Mesh();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                var vertices = new Vector3[3]; var normals = new Vector3[3]; var tangents = new Vector3[3];
                vertices[0] = new Vector3(.001f, 0, 0);
                mesh.AddBlendShapeFrame("ZeroOptional", 100, vertices, normals, tangents);
                normals[1] = new Vector3(.001f, 0, 0); tangents[2] = new Vector3(0, .001f, 0);
                mesh.AddBlendShapeFrame("TinyOptional", 100, vertices, normals, tangents);
                // Compare with the native API result, including any Unity quantization.
                mesh.GetBlendShapeFrameVertices(1, 0, vertices, normals, tangents);
                var extracted = new UMAMeshData(); extracted.RetrieveDataFromUnityMesh(mesh);
                Assert.IsTrue(UMABlendFrame.isAllZero(extracted.blendShapes[0].frames[0].deltaNormals));
                Assert.That(extracted.blendShapes[0].frames[0].deltaNormals?.Length ?? 0, Is.Zero);
                Assert.That(extracted.blendShapes[0].frames[0].deltaTangents?.Length ?? 0, Is.Zero);
                Assert.AreEqual(vertices[0], extracted.blendShapes[0].frames[0].deltaVertices[0]);
                Assert.AreEqual(normals[1], extracted.blendShapes[1].frames[0].deltaNormals[1]);
                Assert.AreEqual(tangents[2], extracted.blendShapes[1].frames[0].deltaTangents[2]);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [TestCase(false)] [TestCase(true)]
        public void AutomaticAndPersistedBakesMatchAllChannelsAndPreservePreviousFrame(bool jobified)
        {
            using var generator = new GeneratorScope();
            using (var fixture = new UMAResourceReuseMeshTests.Fixture())
            {
                var m = Mesh(); fixture.Slot.meshData = m;
                var a = fixture.Avatar(false);
                a.blendShapeSettings.blendShapes["TestShape"] = new BlendShapeData { isBaked = true, value = .75f };
                if (jobified) fixture.BuildJobified(a); else fixture.Build(a);
                var expected = a.GetRenderer(0).sharedMesh;
                var v = expected.vertices; var n = expected.normals; var t = expected.tangents;
                m.PrepareBuildData();
                var b = fixture.Avatar(false);
                b.blendShapeSettings.blendShapes["TestShape"] = new BlendShapeData { isBaked = true, value = .75f };
                if (jobified) fixture.BuildJobified(b); else fixture.Build(b);
                var actual = b.GetRenderer(0).sharedMesh;
                CollectionAssert.AreEqual(v, actual.vertices);
                CollectionAssert.AreEqual(n, actual.normals);
                CollectionAssert.AreEqual(t, actual.tangents);
                Assert.AreEqual(m.vertices[0].y + .1f, actual.vertices[0].y, 1e-6);
                Assert.AreEqual(.1f, actual.normals[1].x, 1e-6);
                Assert.AreEqual(.1f, actual.tangents[2].y, 1e-6);
            }
        }

        [TestCase("MirrorU")] [TestCase("MirrorV")] [TestCase("MirrorUV")]
        [TestCase("Triangles")] [TestCase("LOD")] [TestCase("Bone")]
        public void MutationMethodsInvalidateEvenThroughAliases(string mutation)
        {
            var m = Mesh(); Assert.IsTrue(UMAMeshPreparation.TryGet(m, out var before));
            var alias = m.ShallowCopy(m.vertices);
            switch (mutation)
            {
                case "MirrorU": alias.MirrorU(0); break;
                case "MirrorV": alias.MirrorV(0); break;
                case "MirrorUV": alias.MirrorUV(0); break;
                case "Triangles": m.submeshes[0].SetTriangles(new[] { 2, 1, 0 }); break;
                case "LOD": m.submeshes[0].SetLodRanges(new List<UMALodRange> { new UMALodRange(0, 3) }); break;
                case "Bone":
                    var bone = m.umaBones[0].Duplicate(); bone.position = Vector3.up;
                    m.umaBones[0].Assign(bone); break;
            }
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out var after));
            Assert.AreNotEqual(before.SourceFingerprint, after.SourceFingerprint);
        }

        [Test]
        public void NewGeneratedBuffersDoNotInvalidateExistingSources()
        {
            var m = Mesh(); UMAMeshPreparation.TryGet(m, out var before);
            var output = Mesh(); output.MirrorUV(0);
            output.submeshes[0].SetTriangles(new[] { 0, 1, 2 });
            UMAMeshPreparation.TryGet(m, out var after);
            Assert.AreSame(before, after);
        }

        [Test]
        public void AutomaticConversionDoesNotDirtyLoadedSlot()
        {
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>(); slot.meshData = Mesh();
            try
            {
                UMAMeshPreparation.RegisterOwner(slot);
                int dirty = EditorUtility.GetDirtyCount(slot);
                Assert.IsTrue(UMAMeshPreparation.TryGet(slot.meshData, out var before));
                Assert.AreEqual(dirty, EditorUtility.GetDirtyCount(slot));
                slot.meshData.vertices[0] += Vector3.one; EditorUtility.SetDirty(slot);
                Assert.IsTrue(UMAMeshPreparation.TryGet(slot.meshData, out var after));
                Assert.AreNotEqual(before.SourceFingerprint, after.SourceFingerprint);
            }
            finally { UnityEngine.Object.DestroyImmediate(slot); }
        }

        [Test]
        public void PreparedFingerprintsDoNotChangeExactReuseEquality()
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                f.Slot.meshData = Mesh(); f.Slot.meshData.PrepareBuildData();
                var a = f.Avatar(); var b = f.Avatar(); f.Build(a); f.Build(b);
                Assert.AreSame(a.GetRenderer(0).sharedMesh, b.GetRenderer(0).sharedMesh);
                f.Slot.meshData.blendShapes[0].frames[0].deltaVertices[8] = Vector3.one;
                f.Slot.meshData.InvalidateBuildData();
                var c = f.Avatar(); f.Build(c);
                Assert.AreNotSame(a.GetRenderer(0).sharedMesh, c.GetRenderer(0).sharedMesh);
            }
        }

        [Test]
        public void UpgradeUtilityPersistsAndSaveHookRefreshesEdits()
        {
            string path = "Assets/UMA_SlotPreparationTest_" + Guid.NewGuid().ToString("N") + ".asset";
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>(); slot.meshData = Mesh();
            try
            {
                AssetDatabase.CreateAsset(slot, path);
                Assert.IsTrue(UMA.Editors.UMASlotPreparationUtility.PrepareSlot(slot));
                AssetDatabase.SaveAssets();
                slot.meshData.blendShapes[0].frames[0].deltaVertices[9] = Vector3.right;
                EditorUtility.SetDirty(slot);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var saved = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
                Assert.IsTrue(UMAMeshPreparation.TryGet(saved.meshData, out var p));
                CollectionAssert.Contains(p.BlendShapes[0].Frames[0].AffectedVertices, 9);
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        [TestCase(false)] [TestCase(true)]
        public void SavingAutomaticallyConvertedSlotPersistsCurrentFormat(bool saveIfDirty)
        {
            string path = "Assets/UMA_AutoPreparationTest_" + Guid.NewGuid().ToString("N") + ".asset";
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>(); slot.meshData = Mesh();
            try
            {
                AssetDatabase.CreateAsset(slot, path);
                // Simulate the state of a loaded legacy slot. Do not save this state:
                // the automatic save hook is allowed to upgrade it immediately.
                typeof(UMAMeshData).GetField("preparedData", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(slot.meshData, null);
                EditorUtility.ClearDirty(slot);
                slot.meshData.InvalidateBuildData();
                UMAMeshPreparation.RegisterOwner(slot);
                Assert.IsNull(slot.meshData.PreparedData);
                Assert.IsTrue(UMAMeshPreparation.TryGet(slot.meshData, out var converted));
                Assert.IsTrue(EditorUtility.IsDirty(slot), "Conversion must be eligible for the user's next save.");
                if (saveIfDirty) AssetDatabase.SaveAssetIfDirty(slot); else AssetDatabase.SaveAssets();
                Resources.UnloadAsset(slot);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
                var saved = slot.meshData.PreparedData;
                Assert.NotNull(saved, "The new format must be on disk, not just in the transient cache.");
                Assert.AreEqual(UMAMeshPreparation.CurrentVersion, saved.Version);
                Assert.IsTrue(UMAMeshPreparation.TryGet(slot.meshData, out var verified));
                Assert.AreSame(saved, verified, "Reload must verify saved metadata, not convert it again.");
                Assert.IsFalse(EditorUtility.IsDirty(slot));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
    }
}
