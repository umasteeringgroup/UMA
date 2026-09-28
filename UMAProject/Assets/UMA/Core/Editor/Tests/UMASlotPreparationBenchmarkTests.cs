using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UMA.Tests
{
    /// <summary>Read-only experiments. No extra packed streams/topology are saved in slots.</summary>
    public class UMASlotPreparationBenchmarkTests
    {
        private struct Packed { public Vector3 Normal; public Vector4 Tangent; public Color32 Color; public Vector2 UV0, UV1; }
        private struct Triangle { public int A, B, C; }

        [TestCase("Assets/UMA/UMA3/Races/Slots/UMA30_Body/UMA30_Body_UDIM1001_slot.asset")]
        [TestCase("Assets/UMA/UMA3/Races/Slots/UMA30_Body/UMA30_Body_UDIM1002_slot.asset")]
        [TestCase("Assets/UMA/UMA3/Races/Slots/UMA30_Body/UMA30_Body_UDIM1003_slot.asset")]
        public void MeasurePreparedStreamsAndTopologyWithoutChangingAssets(string path)
        {
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
            Assert.NotNull(slot, "Benchmark must use current checkout source assets.");
            var m = slot.meshData;
            var packed = new Packed[m.vertexCount];
            Action pack = () => {
                for (int i = 0; i < packed.Length; i++) packed[i] = new Packed {
                    Normal = m.normals != null && m.normals.Length == packed.Length ? m.normals[i] : Vector3.zero,
                    Tangent = m.tangents != null && m.tangents.Length == packed.Length ? m.tangents[i] : Vector4.zero,
                    Color = m.colors32 != null && m.colors32.Length == packed.Length ? m.colors32[i] : new Color32(255,255,255,255),
                    UV0 = m.uv != null && m.uv.Length == packed.Length ? m.uv[i] : Vector2.zero,
                    UV1 = m.uv2 != null && m.uv2.Length == packed.Length ? m.uv2[i] : Vector2.zero };
            };
            pack();
            var output = new Packed[packed.Length];
            double packing = Median(pack);
            double copying = Median(() => Array.Copy(packed, output, packed.Length));
            CollectionAssert.AreEqual(packed, output);
            // Native output copies are the closer analogue to the MeshData combiner.
            using var native = new NativeArray<Packed>(packed.Length, Allocator.TempJob);
            double nativeCopy = Median(() => native.CopyFrom(packed));
            var indices = m.submeshes[0].GetBaseTriangles();
            var topology = new Triangle[indices.Length / 3];
            Action prepareTopology = () => {
                for (int i = 0; i < topology.Length; i++) topology[i] = new Triangle { A = indices[i*3], B = indices[i*3+1], C = indices[i*3+2] };
            };
            prepareTopology();
            var topologyOutput = new Triangle[topology.Length];
            double topologyBuild = Median(prepareTopology);
            double topologyCopy = Median(() => Array.Copy(topology, topologyOutput, topology.Length));
            CollectionAssert.AreEqual(topology, topologyOutput);
            using var generator = new UMAMeshPreparationTests.GeneratorScope();
            using var fixture = new UMAResourceReuseMeshTests.Fixture();
            fixture.Slot.meshData = m.DeepCopy(); // Never upgrade/save the real benchmark asset.
            Assert.IsTrue(UMAMeshPreparation.TryGet(fixture.Slot.meshData, out _), "Current UMA 3 source must prepare successfully.");
            Action build = () => {
                var avatar = fixture.Avatar(false);
                avatar.blendShapeSettings.ignoreBlendShapes = true;
                fixture.BuildJobified(avatar);
                UnityEngine.Object.DestroyImmediate(avatar.gameObject);
            };
            build(); // JIT / preparation warmup.
            SkinnedMeshCombinerMeshAPI.ResetTimings();
            for (int i = 0; i < 10; i++) build();
            double actualPacking = (SkinnedMeshCombinerMeshAPI.Ticks_PackNormalsTangents +
                SkinnedMeshCombinerMeshAPI.Ticks_PackColUV01) * 1000.0 / Stopwatch.Frequency / 10;
            Debug.Log($"SLOT_PREPARATION_BENCH {slot.slotName}: vertices={m.vertexCount}, triangles={topology.Length}; " +
                $"pack={packing:F4}ms, prepared-copy={copying:F4}ms, native-copy={nativeCopy:F4}ms, extra-stream-bytes={packed.Length * Marshal.SizeOf<Packed>()}; " +
                $"topology-build={topologyBuild:F4}ms, topology-copy={topologyCopy:F4}ms, extra-topology-bytes={topology.Length * Marshal.SizeOf<Triangle>()}. " +
                $"Current unsafe pack stage={actualPacking:F4}ms (mean of 10 real builds, reuse OFF). " +
                "Copy experiments: median of 7 batches x 50 iterations; allocations excluded. Microbenchmark, not end-to-end build speedup.");
        }

        [Test]
        public void MeasurePreparedColdAndWarmBuilds()
        {
            using var generator = new UMAMeshPreparationTests.GeneratorScope();
            using var f = new UMAResourceReuseMeshTests.Fixture();
            f.Slot.meshData = UMAMeshPreparationTests.Mesh(24000);
            var m = f.Slot.meshData;
            var coldTimer = Stopwatch.StartNew();
            Assert.IsTrue(UMAMeshPreparation.TryGet(m, out _)); coldTimer.Stop();
            Action build = () => {
                var a = f.Avatar(false);
                a.blendShapeSettings.blendShapes["TestShape"] = new BlendShapeData { isBaked = true, value = .75f };
                f.BuildJobified(a);
                UnityEngine.Object.DestroyImmediate(a.gameObject);
            };
            build();
            double automatic = Median(build, 5, 3);
            var timer = Stopwatch.StartNew(); m.PrepareBuildData(); timer.Stop(); double preparation = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); Assert.IsTrue(UMAMeshPreparation.TryGet(m, out _)); timer.Stop(); double verification = timer.Elapsed.TotalMilliseconds;
            double warm = Median(build, 5, 3);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) UMAMeshPreparation.TryGet(m, out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Debug.Log($"SLOT_PREPARATION_BUILD_BENCH 24k vertices: auto-convert={coldTimer.Elapsed.TotalMilliseconds:F4}ms, automatic-warm-build={automatic:F4}ms, persisted-warm-build={warm:F4}ms, prepare={preparation:F4}ms, first-verify={verification:F4}ms; warm metadata 10000 checks={allocated} GC bytes. Reuse OFF, jobified combiner; includes avatar setup and disposal, not a baseline speedup measurement.");
        }

        private static double Median(Action action, int iterations = 50, int batches = 7)
        {
            action();
            var times = new double[batches];
            for (int b = 0; b < batches; b++)
            {
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++) action();
                watch.Stop(); times[b] = watch.Elapsed.TotalMilliseconds / iterations;
            }
            Array.Sort(times); return times[times.Length / 2];
        }
    }
}
