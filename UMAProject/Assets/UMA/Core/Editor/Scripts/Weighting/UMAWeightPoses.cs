using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors.Weighting
{
    /// <summary>
    /// Test poses for weights: a humanoid character's bones turned (walking, sitting, arms raised and forward, a
    /// twist, a wide stance), the body captured in each, and how each bone moved. The character is put back as it
    /// was before this returns.
    /// </summary>
    public static class UMAWeightPoses
    {
        /// <summary>One bone turned about one of the character's own axes (x right, y up, z forward).</summary>
        public struct Turn
        {
            public HumanBodyBones Bone;
            public Vector3 Axis;
            public float Degrees;
            public Turn(HumanBodyBones bone, Vector3 axis, float degrees) { Bone = bone; Axis = axis; Degrees = degrees; }
        }

        public sealed class PoseSpec
        {
            public string Name;
            public Turn[] Turns;
        }

        private static int standardCount = -1;

        /// <summary>How many standard test poses there are.</summary>
        public static int StandardPoseCount => standardCount >= 0 ? standardCount : (standardCount = StandardPoses().Count);

        /// <summary>The standard test poses.</summary>
        public static List<PoseSpec> StandardPoses()
        {
            Vector3 right = Vector3.right, up = Vector3.up, forward = Vector3.forward;
            return new List<PoseSpec>
            {
                new PoseSpec { Name = "Walk, left leg forward", Turns = new[] {
                    new Turn(HumanBodyBones.LeftUpperLeg, right, -30f), new Turn(HumanBodyBones.LeftLowerLeg, right, 25f),
                    new Turn(HumanBodyBones.RightUpperLeg, right, 20f), new Turn(HumanBodyBones.RightLowerLeg, right, 15f) } },
                new PoseSpec { Name = "Walk, right leg forward", Turns = new[] {
                    new Turn(HumanBodyBones.RightUpperLeg, right, -30f), new Turn(HumanBodyBones.RightLowerLeg, right, 25f),
                    new Turn(HumanBodyBones.LeftUpperLeg, right, 20f), new Turn(HumanBodyBones.LeftLowerLeg, right, 15f) } },
                new PoseSpec { Name = "Sitting", Turns = new[] {
                    new Turn(HumanBodyBones.LeftUpperLeg, right, -70f), new Turn(HumanBodyBones.RightUpperLeg, right, -70f),
                    new Turn(HumanBodyBones.LeftLowerLeg, right, 70f), new Turn(HumanBodyBones.RightLowerLeg, right, 70f) } },
                new PoseSpec { Name = "Arms raised", Turns = new[] {
                    new Turn(HumanBodyBones.LeftUpperArm, forward, -40f), new Turn(HumanBodyBones.RightUpperArm, forward, 40f) } },
                new PoseSpec { Name = "Arms forward", Turns = new[] {
                    new Turn(HumanBodyBones.LeftUpperArm, up, 60f), new Turn(HumanBodyBones.RightUpperArm, up, -60f),
                    new Turn(HumanBodyBones.LeftLowerArm, up, 30f), new Turn(HumanBodyBones.RightLowerArm, up, -30f) } },
                new PoseSpec { Name = "Twist and bend", Turns = new[] {
                    new Turn(HumanBodyBones.Spine, up, 25f), new Turn(HumanBodyBones.Chest, right, 20f), new Turn(HumanBodyBones.Neck, right, 10f) } },
                new PoseSpec { Name = "Wide stance", Turns = new[] {
                    new Turn(HumanBodyBones.LeftUpperLeg, forward, -20f), new Turn(HumanBodyBones.RightUpperLeg, forward, 20f) } },
            };
        }

        /// <summary>True when the character can be posed (a humanoid with an Animator avatar).</summary>
        public static bool CanPose(UMASlotSkinTransfer.TargetData skin)
        {
            Animator animator = skin != null && skin.UmaData != null ? skin.UmaData.animator : null;
            return animator != null && animator.avatar != null && animator.avatar.isHuman && skin.Avatar != null;
        }

        /// <summary>
        /// Captures the donor in each pose. <paramref name="skin"/> must be the capture the donor was made from;
        /// its skin matrices are put back afterwards. Returns an empty list (with a reason) for a character that
        /// cannot be posed.
        /// </summary>
        public static List<UMAWeightPose> Capture(UMASlotSkinTransfer.TargetData skin, UMAWeightDonor donor, IList<PoseSpec> specs, out string message)
        {
            message = string.Empty;
            var poses = new List<UMAWeightPose>();
            if (!CanPose(skin)) { message = "The character is not a humanoid, so it cannot be put in test poses."; return poses; }
            if (specs == null) specs = StandardPoses();
            Animator animator = skin.UmaData.animator;
            Transform root = skin.Avatar.transform;
            Matrix4x4[] baseSkin = skin.CurrentSkinMatrices ?? donor.SkinMatrices;
            if (baseSkin == null)
            {
                // The capture has no skin matrices yet: work them out as BuildTargetSurface does.
                Matrix4x4 toLocal = skin.Renderer.transform.worldToLocalMatrix;
                baseSkin = new Matrix4x4[skin.Bones.Length];
                for (int b = 0; b < skin.Bones.Length; b++) baseSkin[b] = toLocal * skin.Bones[b].localToWorldMatrix * skin.BindPoses[b];
            }
            var inverse = new Matrix4x4[baseSkin.Length];
            for (int b = 0; b < baseSkin.Length; b++) inverse[b] = baseSkin[b].inverse;
            Matrix4x4[] keptSkin = skin.CurrentSkinMatrices;
            try
            {
                foreach (PoseSpec spec in specs)
                {
                    var saved = new List<(Transform, Quaternion)>();
                    try
                    {
                        foreach (Turn turn in spec.Turns)
                        {
                            Transform bone = animator.GetBoneTransform(turn.Bone);
                            if (bone == null) continue;
                            saved.Add((bone, bone.localRotation));
                            bone.rotation = Quaternion.AngleAxis(turn.Degrees, root.TransformDirection(turn.Axis)) * bone.rotation;
                        }
                        if (saved.Count == 0) continue;
                        UMASlotSkinTransfer.TargetSurface surface = UMASlotSkinTransfer.BuildTargetSurface(skin);
                        var positions = (Vector3[])donor.Positions.Clone();
                        foreach (UMASlotSkinTransfer.TriangleInfo t in surface.Triangles)
                        {
                            if (t.Index0 < positions.Length) positions[t.Index0] = t.A;
                            if (t.Index1 < positions.Length) positions[t.Index1] = t.B;
                            if (t.Index2 < positions.Length) positions[t.Index2] = t.C;
                        }
                        var matrices = new Matrix4x4[Math.Max(donor.BoneCount, baseSkin.Length)];
                        for (int b = 0; b < matrices.Length; b++)
                            matrices[b] = b < baseSkin.Length && b < surface.CurrentSkinMatrices.Length ? surface.CurrentSkinMatrices[b] * inverse[b] : Matrix4x4.identity;
                        poses.Add(new UMAWeightPose { Name = spec.Name, BoneMatrices = matrices, DonorPositions = positions });
                    }
                    finally
                    {
                        for (int k = saved.Count - 1; k >= 0; k--) saved[k].Item1.localRotation = saved[k].Item2;
                    }
                }
            }
            finally
            {
                skin.CurrentSkinMatrices = keptSkin;
            }
            if (poses.Count == 0) message = "None of the test poses' bones were found on the character.";
            return poses;
        }

        /// <summary>Where the target's vertices go in a pose with some weights (per vertex, donor bone indices).</summary>
        public static Vector3[] SkinTarget(Vector3[] rest, BoneWeight1[][] weights, UMAWeightPose pose)
        {
            var result = new Vector3[rest.Length];
            for (int v = 0; v < rest.Length; v++) result[v] = UMAWeightScoring.Skin(rest[v], weights[v], pose.BoneMatrices);
            return result;
        }
    }
}
