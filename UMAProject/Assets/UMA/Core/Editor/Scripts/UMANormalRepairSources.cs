using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>Converts race-baked normal edits back to their persistent shared sources.</summary>
    [InitializeOnLoad]
    public static class UMANormalRepairSources
    {
        private static readonly Dictionary<SlotDataAsset, Vector3[]> tracked = new Dictionary<SlotDataAsset, Vector3[]>();
        static UMANormalRepairSources() { Undo.undoRedoPerformed += RefreshBakedCopies; }

        public static SlotDataAsset Resolve(SlotDataAsset asset)
        {
            if (asset == null) return null;
            if (EditorUtility.IsPersistent(asset)) return asset;
            if (!asset.isBaked || UMAAssetIndexer.Instance == null) return null;
            var source = UMAAssetIndexer.Instance.GetAsset<SlotDataAsset>(asset.sourceSlot);
            return source != null && EditorUtility.IsPersistent(source) ? source : null;
        }

        public static Dictionary<SlotDataAsset, Vector3[]> ToSourceNormals(
            Dictionary<SlotDataAsset, Vector3[]> edits, RaceData race)
        {
            var result = new Dictionary<SlotDataAsset, Vector3[]>();
            foreach (var pair in edits)
            {
                var asset = pair.Key; var source = Resolve(asset);
                if (source == null) throw new InvalidOperationException("No persistent source slot found for " + asset.slotName + ".");
                Vector3[] normals;
                if (source == asset) normals = pair.Value;
                else
                {
                    if (race == null) throw new InvalidOperationException("Race is required to verify baked slot " + asset.slotName);
                    var expected = source.BakeNewSlotData(new SlotDataAsset.BakeSlotParams
                    {
                        burnOptions = race.PrebakedBlendshapes, ShapesToInclude = race.UnbakedShapesToInclude,
                        copyUnbakedBlendshapes = race.UnbakedShapesToInclude != null && race.UnbakedShapesToInclude.Count > 0,
                        smoothingAngleDegrees = -1, newSlotName = asset.slotName, addToIndexer = false,
                        forceRebuildRaceSlots = true
                    });
                    try
                    {
                        if (expected == source || expected.meshData.vertices.Length != asset.meshData.vertices.Length ||
                            source.meshData.normals.Length != pair.Value.Length ||
                            !Same(expected.meshData.vertices, asset.meshData.vertices) || !Same(expected.meshData.normals, asset.meshData.normals))
                            throw new InvalidOperationException("Baked slot no longer matches its source/race: " + asset.slotName + ". Rebuild the character and scan again.");
                        normals = ConvertBakedNormals(source.meshData.normals, asset.meshData.normals, pair.Value);
                    }
                    finally { if (expected != null && expected != source) UnityEngine.Object.DestroyImmediate(expected); }
                }
                if (result.TryGetValue(source, out var prior) && !Same(prior, normals))
                    throw new InvalidOperationException("Two equipped slots request different changes to shared source " + source.slotName + ". Fix them separately.");
                result[source] = normals;
            }
            return result;
        }

        private static bool Same(Vector3[] a, Vector3[] b) => a.Length == b.Length &&
            a.Select((v, i) => (v - b[i]).sqrMagnitude < 1e-10f).All(v => v);

        public static Vector3[] ConvertBakedNormals(Vector3[] source, Vector3[] baked, Vector3[] desired)
        {
            if (source.Length != baked.Length || source.Length != desired.Length)
                throw new InvalidOperationException("Baked/source vertex counts differ.");
            var output = (Vector3[])source.Clone();
            for (int i = 0; i < output.Length; i++)
            {
                if (desired[i] == baked[i]) continue;
                // UMA adds blendshape normal deltas when baking. Subtract the unchanged bake
                // contribution rather than writing a race-deformed normal into the base mesh.
                // Retain baked magnitude: normalizing the source here would change the rebake.
                output[i] += desired[i].normalized * baked[i].magnitude - baked[i];
                if (output[i].sqrMagnitude < 1e-12f)
                    throw new InvalidOperationException("This baked correction would produce a zero base normal at vertex " + i + ".");
            }
            return output;
        }

        public static void Track(IEnumerable<SlotDataAsset> sources)
        {
            foreach (var source in sources) tracked[source] = (Vector3[])source.meshData.normals.Clone();
        }

        public static void RefreshBakedCopies()
        {
            if (tracked.Count == 0) return;
            var copies = Resources.FindObjectsOfTypeAll<SlotDataAsset>();
            foreach (var source in tracked.Keys.ToArray())
            {
                if (source == null) { tracked.Remove(source); continue; }
                var old = tracked[source]; var current = source.meshData.normals;
                if (old.Length != current.Length) { tracked.Remove(source); continue; }
                if (old.SequenceEqual(current)) continue;
                foreach (var copy in copies)
                {
                    if (copy == null || !copy.isBaked || EditorUtility.IsPersistent(copy) ||
                        copy.sourceSlot != source.slotName || copy.meshData?.normals?.Length != old.Length) continue;
                    var updated = (Vector3[])copy.meshData.normals.Clone();
                    for (int i = 0; i < old.Length; i++) updated[i] += current[i] - old[i];
                    UMANormalRepair.SetNormals(copy, updated);
                }
                source.meshData.InvalidateBuildData();
                tracked[source] = (Vector3[])current.Clone();
            }
        }
    }
}
