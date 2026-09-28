using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    // Snapshot the incoming population before moving any curve. A clump must not depend on
    // which strand was evaluated first, or attract toward an already-deformed neighbor.
    internal sealed class HairClumpField
    {
        private readonly Dictionary<Vector3Int, HairEvaluatedCurve> cells = new Dictionary<Vector3Int, HairEvaluatedCurve>();
        private readonly List<HairEvaluatedCurve> centers = new List<HairEvaluatedCurve>();
        private readonly List<Vector3> roots = new List<Vector3>();
        private readonly HairPointSpatialIndex index = new HairPointSpatialIndex();
        private float radius;
        internal void Clear()
        {
            cells.Clear(); centers.Clear(); centers.Capacity = 0;
            roots.Clear(); roots.Capacity = 0; index.Clear();
        }
        internal void Prepare(IReadOnlyList<HairEvaluatedCurve> population, HairModifierSettings modifier)
        {
            cells.Clear(); roots.Clear(); radius = Mathf.Max(0.001f, modifier.clumpRadius);
            Vector3 offset = HairStrandNoise.Sample(0, modifier.seed) * radius;
            foreach (var curve in population)
            {
                if (curve.points.Count < 2) continue;
                Vector3 p = (curve.points[0].position + offset) / radius;
                var cell = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
                if (!cells.TryGetValue(cell, out var old) || curve.seed < old.seed ||
                    (curve.seed == old.seed && string.CompareOrdinal(curve.curveId, old.curveId) < 0)) cells[cell] = curve;
            }
            foreach (var source in cells.Values)
            {
                int i = roots.Count;
                if (centers.Count <= i) centers.Add(new HairEvaluatedCurve());
                source.CopyTo(centers[i]); roots.Add(source.points[0].position);
            }
            index.Rebuild(roots);
        }
        internal HairEvaluatedCurve Target(HairEvaluatedCurve curve, Vector3? helper)
        {
            Span<HairNearestPoint> nearest = stackalloc HairNearestPoint[16];
            int count = index.QueryNearest(helper ?? curve.points[0].position, nearest);
            for (int i = 0; i < count; i++)
            {
                var candidate = centers[nearest[i].index];
                if (!helper.HasValue && (nearest[i].distanceSquared > radius * radius * 9f || Vector3.Dot(candidate.rootNormal, curve.rootNormal) < 0.1f)) continue;
                return candidate;
            }
            return null;
        }
    }
}
