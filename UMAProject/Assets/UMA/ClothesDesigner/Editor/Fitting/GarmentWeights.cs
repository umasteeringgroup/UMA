using System;
using System.Collections.Generic;
using UMA.Editors;
using UMA.Editors.Weighting;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Bone weights for baked clothes, from the shared weighting library (<see cref="UMAWeightTransfer"/>) with the
    /// fit's method. Each piece copies from its own part of the body: a piece laid round an arm from all of it, the
    /// others (with <see cref="ClothesFit.donorsExcludeArms"/>) from the body without the arms. Auto and Simulated
    /// Fit pose the character; Simulated Fit also drapes the clothes in each pose with the designer's solver.
    /// </summary>
    public static class GarmentWeights
    {
        /// <summary>Simulated frames per pose for Simulated Fit (a second and a half).</summary>
        public static int SettleFrames = 90;

        /// <summary>The poses of one bake: the body in each, and for Simulated Fit the drape in each (on the drape's mesh).</summary>
        public sealed class Poses
        {
            public List<UMAWeightPose> Body = new List<UMAWeightPose>();
            public PatternMesh DrapeMesh;
            public List<Vector3[]> Drapes;
            public double Milliseconds;
        }

        /// <summary>The library's settings for a fit.</summary>
        public static UMAWeightSettings Settings(ClothesFit fit, BodyReference body)
        {
            var s = new UMAWeightSettings
            {
                Method = fit.weightMethod,
                MatchDistance = Mathf.Max(0.005f, fit.weightMatchCm * 0.01f),
                MatchAngleDegrees = fit.weightMatchAngle,
                MaxBones = Mathf.Clamp(fit.weightMaxBones, 1, 8),
                MirrorLeftRight = fit.weightMirror,
                // The cloth's own UVs are the pattern's, never the body's.
                ExactUseUV = false
            };
            if (s.MirrorLeftRight && body != null && body.Skin != null && body.Skin.Renderer != null && body.Skin.Avatar != null) s.SetMirrorFrom(body.Skin.Renderer.transform, body.Skin.Avatar.transform);
            return s;
        }

        /// <summary>Poses the bake needs for the fit's method (none for the others). Shows progress; may take a while for Simulated Fit.</summary>
        public static Poses Prepare(FitSession session, PatternMesh drapeMesh, Vector3[] drape, List<string> messages)
        {
            ClothesFit fit = session.Fit;
            if (fit.weightMethod != UMAWeightMethod.Auto && fit.weightMethod != UMAWeightMethod.SimulatedFit) return null;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            BodyReference body = session.Body;
            var poses = new Poses();
            poses.Body = UMAWeightPoses.Capture(body.Skin, body.WeightDonor, null, out string message);
            if (poses.Body.Count == 0)
            {
                messages.Add(message + " " + UMAWeightSettings.DisplayName(fit.weightMethod) + " used Robust Inpaint.");
                return null;
            }
            if (fit.weightMethod == UMAWeightMethod.SimulatedFit)
            {
                try
                {
                    poses.DrapeMesh = drapeMesh;
                    poses.Drapes = DrapeInPoses(session, drapeMesh, drape, poses.Body, messages);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }
            poses.Milliseconds = timer.Elapsed.TotalMilliseconds;
            return poses;
        }

        /// <summary>
        /// The cloth in each pose: skinned there with Robust Inpaint weights, then simulated against the posed body
        /// until it hangs (no pins: the pins are for the pose the clothes were made in).
        /// </summary>
        private static List<Vector3[]> DrapeInPoses(FitSession session, PatternMesh drapeMesh, Vector3[] drape, List<UMAWeightPose> bodyPoses, List<string> messages)
        {
            ClothesFit fit = session.Fit;
            BodyReference body = session.Body;
            int[] group = GarmentBaker.WeldGroups(drapeMesh.VertexCount, drapeMesh.stitches);
            UMAWeightSettings start = Settings(fit, body);
            start.Method = UMAWeightMethod.RobustInpaint;
            UMAWeightTarget startTarget = Target(session, drapeMesh, drape, group);
            UMAWeightResult initial = UMAWeightTransfer.Transfer(body.WeightDonor, startTarget, WithRegions(start, session, drapeMesh, startTarget));
            BoneWeight1[][] perVertex = initial.PerVertex();
            Vector3 gravity = body.Skin.Renderer != null ? body.Skin.Renderer.transform.InverseTransformDirection(Physics.gravity) : Physics.gravity;
            SimParams p = ClothModel.Params(Mathf.Max(fit.damping, 4f), fit.clearanceCm * 0.01f, 0f, 1f, gravity, false, drapeMesh.edgeLengthCm);
            p.lateClosure = 0f;
            p.thickness = fit.clothThicknessMm * 0.001f;
            p.selfCollide = fit.clothThicknessMm > 0f ? 1 : 0;
            var drapes = new List<Vector3[]>();
            bool gpu = !session.PreferCpu && ClothSolverGpu.Supported;
            UMASlotSkinTransfer.TriangleInfo[] triangles = body.Surface.Triangles;
            for (int k = 0; k < bodyPoses.Count; k++)
            {
                UMAWeightPose pose = bodyPoses[k];
                EditorUtility.DisplayProgressBar("Simulated Fit", "Draping the clothes: " + pose.Name + " (" + (k + 1) + " of " + bodyPoses.Count + ")", (float)k / bodyPoses.Count);
                var posedTriangles = new UMASlotSkinTransfer.TriangleInfo[triangles.Length];
                for (int t = 0; t < triangles.Length; t++)
                {
                    UMASlotSkinTransfer.TriangleInfo o = triangles[t];
                    posedTriangles[t] = new UMASlotSkinTransfer.TriangleInfo(o.RendererIndex, o.RendererName, o.SlotIndex, o.SlotName, o.SubmeshIndex, o.SubmeshTriangleIndex, o.Index0, o.Index1, o.Index2,
                        pose.DonorPositions[o.Index0], pose.DonorPositions[o.Index1], pose.DonorPositions[o.Index2]);
                }
                var collider = new BodyCollider(posedTriangles);
                Vector3[] posed = UMAWeightPoses.SkinTarget(drape, perVertex, pose);
                IClothSolver solver = gpu ? (IClothSolver)new ClothSolverGpu() : new ClothSolverCpu();
                try
                {
                    try { solver.Initialize(session.Model, collider, posed); }
                    catch (Exception e)
                    {
                        solver.Dispose();
                        gpu = false;
                        messages.Add("The GPU solver could not start for Simulated Fit (" + e.Message + "); it used the CPU solver.");
                        solver = new ClothSolverCpu();
                        solver.Initialize(session.Model, collider, posed);
                    }
                    solver.Step(p, SettleFrames);
                    var result = new Vector3[session.Model.ParticleCount];
                    solver.GetPositions(result);
                    drapes.Add(GarmentBaker.Weld(result, group));
                }
                finally
                {
                    solver.Dispose();
                }
            }
            messages.Add("Simulated Fit draped the clothes in " + bodyPoses.Count + " poses (" + SettleFrames + " frames each, " + (gpu ? "GPU" : "CPU") + ").");
            return drapes;
        }

        private static UMAWeightTarget Target(FitSession session, PatternMesh mesh, Vector3[] positions, int[] group) => new UMAWeightTarget
        {
            Positions = positions,
            Triangles = mesh.triangles.ToArray(),
            Groups = group
        };

        /// <summary>Per-piece body regions: pieces laid round an arm copy from all of the body, the others (if asked) without the arms.</summary>
        private static UMAWeightSettings WithRegions(UMAWeightSettings settings, FitSession session, PatternMesh mesh, UMAWeightTarget target)
        {
            ClothesFit fit = session.Fit;
            if (!fit.donorsExcludeArms) return settings;
            BodyReference body = session.Body;
            int triangles = body.WeightDonor.TriangleCount;
            var noArms = new bool[triangles];
            bool anyArm = false;
            for (int t = 0; t < triangles; t++) { noArms[t] = !body.IsArmTriangle(t); anyArm |= !noArms[t]; }
            if (!anyArm) return settings;
            settings.RegionTriangles = new List<bool[]> { null, noArms };
            if (target != null) target.Region = Regions(session, mesh);
            return settings;
        }

        /// <summary>Each vertex's region: 0 (all the body) for pieces laid round an arm, 1 (no arms) for the rest.</summary>
        private static int[] Regions(FitSession session, PatternMesh mesh)
        {
            int n = mesh.VertexCount;
            var region = new int[n];
            ClothesPattern pattern = session.Pattern;
            bool perPiece = mesh.piece.Count == n;
            bool sleeves = GarmentBaker.HasArmPieces(pattern, session.Fit);
            for (int v = 0; v < n; v++)
            {
                bool arm = sleeves;
                if (perPiece)
                {
                    int index = mesh.piece[v];
                    PatternPiece piece = index >= 0 && index < pattern.pieces.Count ? pattern.pieces[index] : null;
                    PiecePlacement placement = piece != null ? session.Fit.PlacementFor(piece) : null;
                    arm = placement != null && PiecePlacement.IsArm(placement.part);
                }
                region[v] = arm ? 0 : 1;
            }
            return region;
        }

        /// <summary>
        /// Weights for one slot mesh (the main slot or a LOD): positions welded, <paramref name="group"/> its sewn
        /// groups. <paramref name="carry"/> takes positions on the drape's mesh to this mesh (null when it is the drape's).
        /// </summary>
        public static UMAWeightResult Transfer(FitSession session, PatternMesh mesh, Vector3[] positions, int[] group, Poses poses, Func<Vector3[], Vector3[]> carry, List<string> messages)
        {
            BodyReference body = session.Body;
            UMAWeightSettings settings = Settings(session.Fit, body);
            UMAWeightTarget target = Target(session, mesh, positions, group);
            WithRegions(settings, session, mesh, target);
            if (poses != null && poses.Body.Count > 0)
            {
                var list = new List<UMAWeightPose>(poses.Body.Count);
                for (int k = 0; k < poses.Body.Count; k++)
                {
                    UMAWeightPose b = poses.Body[k];
                    var pose = new UMAWeightPose { Name = b.Name, BoneMatrices = b.BoneMatrices, DonorPositions = b.DonorPositions };
                    if (poses.Drapes != null && k < poses.Drapes.Count)
                    {
                        Vector3[] onMesh = carry != null && mesh != poses.DrapeMesh ? carry(poses.Drapes[k]) : poses.Drapes[k];
                        if (onMesh != null && onMesh.Length == positions.Length) pose.TargetPositions = onMesh;
                    }
                    list.Add(pose);
                }
                settings.Poses = list;
            }
            else if (settings.Method == UMAWeightMethod.SimulatedFit || settings.Method == UMAWeightMethod.Auto)
            {
                settings.Method = UMAWeightMethod.RobustInpaint;
            }
            UMAWeightResult result = UMAWeightTransfer.Transfer(body.WeightDonor, target, settings);
            messages.Add("Weights: " + result.Report.Summary());
            foreach (UMAWeightScore score in result.Report.Scores) messages.Add("  " + (score.Method == result.Report.Used ? "chosen: " : "tried: ") + score);
            foreach (string m in result.Report.Messages) messages.Add("  " + m);
            return result;
        }
    }
}
