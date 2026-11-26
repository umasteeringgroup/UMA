using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.Editors.Weighting
{
    /// <summary>
    /// Measures weights in poses: the clothes skinned with them against the body in the same pose. Counts cloth
    /// that goes into the body, and how much the cloth's edges stretch or squash.
    /// </summary>
    public static class UMAWeightScoring
    {
        /// <summary>Where a node goes in a pose with some weights (linear blend skinning).</summary>
        public static Vector3 Skin(Vector3 rest, BoneWeight1[] weights, Matrix4x4[] boneMatrices)
        {
            if (weights == null || weights.Length == 0) return rest;
            Vector3 p = Vector3.zero;
            float total = 0f;
            foreach (BoneWeight1 w in weights)
            {
                if ((uint)w.boneIndex >= (uint)boneMatrices.Length) continue;
                p += boneMatrices[w.boneIndex].MultiplyPoint3x4(rest) * w.weight;
                total += w.weight;
            }
            return total > 0f ? p / total : rest;
        }

        /// <summary>The donor's surface in a pose (worked out once per pose).</summary>
        internal static UMAWeightSurface SurfaceOf(UMAWeightPose pose, UMAWeightDonor donor)
        {
            if (pose.ScoringSurface == null)
            {
                pose.ScoringNormals = UMAWeightDonor.ComputeNormals(pose.DonorPositions, donor.Triangles);
                pose.ScoringSurface = new UMAWeightSurface(pose.DonorPositions, donor.Triangles, pose.ScoringNormals);
            }
            return pose.ScoringSurface;
        }

        internal static UMAWeightScore Score(UMAWeightGraph graph, UMAWeightDonor donor, BoneWeight1[][] nodeWeights, IList<UMAWeightPose> poses, float insideTolerance)
        {
            var score = new UMAWeightScore();
            if (poses == null || poses.Count == 0) return score;
            int nodes = graph.NodeCount;
            List<Vector2Int> edges = graph.Edges();
            var restLength = new float[edges.Count];
            for (int e = 0; e < edges.Count; e++) restLength[e] = (graph.Position[edges[e].x] - graph.Position[edges[e].y]).magnitude;
            double insideSum = 0, stretchSum = 0;
            int stretchCount = 0;
            foreach (UMAWeightPose pose in poses)
            {
                UMAWeightSurface surface = SurfaceOf(pose, donor);
                var posed = new Vector3[nodes];
                var depth = new float[nodes];
                Parallel.For(0, nodes, () => surface.NewQuery(), (i, state, q) =>
                {
                    posed[i] = Skin(graph.Position[i], nodeWeights[i], pose.BoneMatrices);
                    UMAWeightHit hit = surface.Closest(posed[i], q);
                    depth[i] = hit.IsInside(insideTolerance) ? hit.Distance : 0f;
                    return q;
                }, _ => { });
                int inside = 0;
                for (int i = 0; i < nodes; i++)
                {
                    if (depth[i] <= 0f) continue;
                    inside++;
                    score.MaxInsideDepth = Mathf.Max(score.MaxInsideDepth, depth[i]);
                }
                insideSum += nodes > 0 ? (double)inside / nodes : 0;
                for (int e = 0; e < edges.Count; e++)
                {
                    if (restLength[e] < 1e-5f) continue;
                    float s = Mathf.Abs((posed[edges[e].x] - posed[edges[e].y]).magnitude / restLength[e] - 1f);
                    stretchSum += s;
                    stretchCount++;
                    score.MaxStretch = Mathf.Max(score.MaxStretch, s);
                }
                score.Poses++;
            }
            score.InsideShare = (float)(insideSum / Math.Max(1, score.Poses));
            score.MeanStretch = (float)(stretchSum / Math.Max(1, stretchCount));
            return score;
        }
    }
}
