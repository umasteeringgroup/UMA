#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public class UMANormalRepairTests
    {
        private readonly List<SlotDataAsset> assets = new List<SlotDataAsset>();

        private SlotDataAsset Slot(string name, Vector3 normal, params Vector3[] positions)
        {
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            assets.Add(slot); slot.name = name;
            slot.meshData = new UMAMeshData
            {
                vertices = positions, normals = positions.Select(_ => normal).ToArray(),
                tangents = positions.Select(_ => new Vector4(1, 0, 0, -1)).ToArray(),
                vertexCount = positions.Length, subMeshCount = 1,
                submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2 }) }
            };
            return slot;
        }

        private (SlotDataAsset a, SlotDataAsset b) Pair()
        {
            return (Slot("torso", Vector3.up, Vector3.zero, Vector3.right, Vector3.forward),
                Slot("legs", Vector3.forward, Vector3.zero, Vector3.left, Vector3.back));
        }

        [TearDown]
        public void TearDown() { foreach (var a in assets) if (a != null) Object.DestroyImmediate(a); assets.Clear(); }

        [Test]
        public void ScanSeparatesSlotPairsAndHonoursSelection()
        {
            var pair = Pair();
            var arms = Slot("arms", Vector3.left, Vector3.right, Vector3.right * 2, Vector3.right * 2 + Vector3.up);
            var scan = UMANormalRepair.Scan(new[] { pair.a, pair.b, arms }, .0001f, 1);
            Assert.That(scan.Groups.Count, Is.EqualTo(2));
            Assert.That(scan.Groups.All(g => g.Seams.Count == 1), Is.True);
            Assert.That(UMANormalRepair.Scan(new[] { pair.b, arms }, .0001f, 1).Groups, Is.Empty);
        }

        [Test]
        public void AverageIsNormalisedIdempotentAndDoesNotModifySources()
        {
            var pair = Pair(); var scan = UMANormalRepair.Scan(new[] { pair.a, pair.b }, .001f, 1);
            scan.Groups[0].Pending = true;
            var result = scan.Calculate();
            Assert.That(Vector3.Distance(result[pair.a][0], (Vector3.up + Vector3.forward).normalized), Is.LessThan(1e-6));
            Assert.That(result[pair.a][0], Is.EqualTo(result[pair.b][0]));
            Assert.That(result[pair.a], Is.EqualTo(scan.Calculate()[pair.a]));
            Assert.That(pair.a.meshData.normals[0], Is.EqualTo(Vector3.up));
            Assert.That(result[pair.a][1], Is.EqualTo(Vector3.up));
        }

        [TestCase(0)] [TestCase(1)]
        public void CopyCanUseEitherSlot(int source)
        {
            var pair = Pair(); var scan = UMANormalRepair.Scan(new[] { pair.a, pair.b }, .001f, 1);
            var g = scan.Groups[0]; g.Pending = true; g.Mode = UMANormalRepair.FixMode.Copy; g.Source = source;
            var result = scan.Calculate();
            Assert.That(result[pair.a][0], Is.EqualTo(source == 0 ? Vector3.up : Vector3.forward));
            Assert.That(result[pair.a][0], Is.EqualTo(result[pair.b][0]));
        }

        [Test]
        public void SmoothingChangesBothNeighbourhoodsAndPinsTheSeam()
        {
            var pair = Pair(); var scan = UMANormalRepair.Scan(new[] { pair.a, pair.b }, .001f, 1);
            var g = scan.Groups[0]; g.Pending = true; g.Smooth = true; g.Strength = 1; g.Rings = 1;
            var result = scan.Calculate();
            Assert.That(Vector3.Angle(result[pair.a][1], result[pair.a][0]), Is.LessThan(45));
            Assert.That(Vector3.Angle(result[pair.b][1], result[pair.b][0]), Is.LessThan(45));
            Assert.That(result[pair.a][0], Is.EqualTo(result[pair.b][0]));
            Assert.That(result.Values.SelectMany(v => v).All(n => Mathf.Abs(n.magnitude - 1) < 1e-5), Is.True);
        }

        [Test]
        public void DuplicateUvVerticesDoNotBiasAverageOrInflateSeamCount()
        {
            var pair = Pair();
            pair.a.meshData.vertices = new[] { Vector3.zero, Vector3.zero, Vector3.right };
            var scan = UMANormalRepair.Scan(new[] { pair.a, pair.b }, .001f, 1);
            scan.Groups[0].Pending = true; var result = scan.Calculate();
            Assert.That(scan.Groups[0].Seams.Count, Is.EqualTo(1));
            Assert.That(result[pair.a][0], Is.EqualTo(result[pair.a][1]));
            Assert.That(Vector3.Angle(result[pair.a][0], Vector3.up), Is.EqualTo(45).Within(.001));
        }

        [Test]
        public void ToleranceCrossesHashCellBoundariesButRejectsDistantPoints()
        {
            var pair = Pair(); pair.a.meshData.vertices[0] = new Vector3(.00099f, 0, 0);
            pair.b.meshData.vertices[0] = new Vector3(.00101f, 0, 0);
            Assert.That(UMANormalRepair.Scan(new[] { pair.a, pair.b }, .001f, 1).Groups.Count, Is.EqualTo(1));
            Assert.That(UMANormalRepair.Scan(new[] { pair.a, pair.b }, .000001f, 1).Groups, Is.Empty);
        }

        [Test]
        public void ScanCanCancelAndStaleDataCannotApply()
        {
            var pair = Pair();
            Assert.Throws<OperationCanceledException>(() => UMANormalRepair.Scan(new[] { pair.a, pair.b }, .001f, 1, _ => true));
            var scan = UMANormalRepair.Scan(new[] { pair.a, pair.b }, .001f, 1);
            pair.a.meshData.normals[0] = Vector3.left;
            Assert.Throws<InvalidOperationException>(() => scan.Calculate());
        }

        [Test]
        public void ConflictingThreeSlotJunctionFailsInsteadOfDependingOnGroupOrder()
        {
            var pair = Pair();
            var third = Slot("third", Vector3.right, Vector3.zero, Vector3.one, Vector3.one * 2);
            var scan = UMANormalRepair.Scan(new[] { pair.a, pair.b, third }, .001f, 1);
            foreach (var g in scan.Groups) g.Pending = true;
            Assert.Throws<InvalidOperationException>(() => scan.Calculate());
            Assert.That(pair.a.meshData.normals[0], Is.EqualTo(Vector3.up));
        }

        [TestCase(false)] [TestCase(true)]
        public void PreviewUsesClonesRestoresReferencesAndCleansUpEvenOnFailure(bool fail)
        {
            var pair = Pair(); var scan = UMANormalRepair.Scan(new[] { pair.a, pair.b }, .001f, 1);
            scan.Groups[0].Pending = true;
            var slots = new[] { new SlotData(pair.a), new SlotData(pair.b) };
            SlotDataAsset clone = null;
            Action run = () => UMANormalViewerEditor.WithTemporarySlots(slots, scan.Calculate(), () =>
            {
                clone = slots[0].asset;
                Assert.That(clone, Is.Not.SameAs(pair.a));
                Assert.That(clone.meshData, Is.Not.SameAs(pair.a.meshData));
                Assert.That(clone.meshData.normals[0], Is.EqualTo(slots[1].asset.meshData.normals[0]));
                Assert.That(pair.a.meshData.normals[0], Is.EqualTo(Vector3.up));
                if (fail) throw new InvalidOperationException("Simulated build failure");
            });
            if (fail) Assert.Throws<InvalidOperationException>(() => run()); else run();
            Assert.That(slots[0].asset, Is.SameAs(pair.a));
            Assert.That(slots[1].asset, Is.SameAs(pair.b));
            Assert.That(clone == null, Is.True);
            Assert.That(pair.a.meshData.normals[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void TangentsRemainOrthogonalAndPreserveHandedness()
        {
            var pair = Pair();
            UMANormalRepair.SetNormals(pair.a, Enumerable.Repeat(new Vector3(1, 1, 0).normalized, 3).ToArray());
            Assert.That(Vector3.Dot(pair.a.meshData.normals[0], pair.a.meshData.tangents[0]), Is.EqualTo(0).Within(1e-6));
            Assert.That(pair.a.meshData.tangents[0].w, Is.EqualTo(-1));
        }

        [Test]
        public void InvalidInputsAreRejectedWithoutMutation()
        {
            var pair = Pair();
            Assert.Throws<ArgumentException>(() => UMANormalRepair.Scan(new[] { pair.a, pair.b }, float.NaN, 1));
            pair.b.meshData.normals[0] = Vector3.zero;
            Assert.Throws<InvalidOperationException>(() => UMANormalRepair.Scan(new[] { pair.a, pair.b }, .001f, 1));
            Assert.That(pair.a.meshData.normals[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void BakedCorrectionPreservesAdditiveBlendshapeContribution()
        {
            var source = new[] { Vector3.up, Vector3.right };
            var delta = new Vector3(.1f, -.2f, .3f);
            var baked = new[] { source[0] + delta, source[1] };
            var desired = new[] { Vector3.forward, source[1] };
            var corrected = UMANormalRepairSources.ConvertBakedNormals(source, baked, desired);
            Assert.That(Vector3.Distance((corrected[0] + delta).normalized, desired[0]), Is.LessThan(1e-6));
            Assert.That(corrected[1], Is.EqualTo(source[1]));
            Assert.That(source[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void ApplySavesNativeSlotAssetsAndSupportsUndoRedo()
        {
            var pair = Pair();
            string folder = "Assets/NormalRepairTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            try
            {
                AssetDatabase.CreateAsset(pair.a, folder + "/torso.asset");
                AssetDatabase.CreateAsset(pair.b, folder + "/legs.asset");
                var scan = UMANormalRepair.Scan(new[] { pair.a, pair.b }, .001f, 1);
                scan.Groups[0].Pending = true;
                Vector3 expected = (Vector3.up + Vector3.forward).normalized;
                var baked = Object.Instantiate(pair.a); assets.Add(baked); baked.isBaked = true;
                typeof(SlotDataAsset).GetField("_sourceSlotName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(baked, pair.a.slotName);
                var delta = new Vector3(.05f, .1f, .15f);
                baked.meshData.normals = baked.meshData.normals.Select(n => n + delta).ToArray();
                UMANormalRepairSources.Track(new[] { pair.a, pair.b });
                Undo.IncrementCurrentGroup();
                UMANormalViewerEditor.SaveRepairs(scan.Calculate());
                UMANormalRepairSources.RefreshBakedCopies();
                Assert.That(Vector3.Distance(baked.meshData.normals[0], expected + delta), Is.LessThan(1e-6));
                Undo.FlushUndoRecordObjects();
                Assert.That(AssetDatabase.CopyAsset(folder + "/torso.asset", folder + "/saved.asset"), Is.True);
                var saved = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(folder + "/saved.asset");
                Assert.That(Vector3.Distance(saved.meshData.normals[0], expected), Is.LessThan(1e-6));
                Undo.PerformUndo();
                Assert.That(pair.a.meshData.normals[0], Is.EqualTo(Vector3.up));
                Assert.That(pair.b.meshData.normals[0], Is.EqualTo(Vector3.forward));
                Assert.That(Vector3.Distance(baked.meshData.normals[0], Vector3.up + delta), Is.LessThan(1e-6));
                Undo.PerformRedo();
                Assert.That(Vector3.Distance(pair.a.meshData.normals[0], expected), Is.LessThan(1e-6));
                Assert.That(pair.a.meshData.normals[0], Is.EqualTo(pair.b.meshData.normals[0]));
                Assert.That(Vector3.Distance(baked.meshData.normals[0], expected + delta), Is.LessThan(1e-6));
            }
            finally
            {
                Undo.ClearUndo(pair.a); Undo.ClearUndo(pair.b);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
#endif
