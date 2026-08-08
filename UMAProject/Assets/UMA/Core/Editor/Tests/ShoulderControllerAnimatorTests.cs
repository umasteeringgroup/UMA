#if UNITY_EDITOR

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class ShoulderControllerAnimatorTests
    {
        private sealed class TestRig
        {
            public GameObject Avatar;
            public UMAData Data;
            public Transform Root;
            public Transform Global;
            public Transform Position;
            public Transform Chest;
            public Transform OppositeShoulder;
            public Transform Shoulder;
            public Transform Arm;
            public Transform LowerArm;
            public Transform Hand;
            public ShoulderControllerAnimator Asset;
            public ShoulderControllerRuntime Runtime;
        }

        private readonly List<TestRig> _rigs = new List<TestRig>();

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < _rigs.Count; index++)
            {
                TestRig rig = _rigs[index];
                if (rig.Asset != null)
                {
                    Object.DestroyImmediate(rig.Asset);
                }

                if (rig.Avatar != null)
                {
                    Object.DestroyImmediate(rig.Avatar);
                }
            }

            _rigs.Clear();
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void TwoBoneElbowSolvePreservesBothBoneLengths()
        {
            Vector3 root = Vector3.zero;
            Vector3 target = new Vector3(0.8f, 0.1f, 0f);
            const float upperLength = 0.6f;
            const float lowerLength = 0.55f;

            Vector3 elbow;
            Vector3 bendDirection;
            bool solved = ShoulderControllerRuntime.TryCalculateTwoBoneElbow(
                root,
                target,
                new Vector3(0.35f, 0.45f, 0.1f),
                Vector3.up,
                upperLength,
                lowerLength,
                out elbow,
                out bendDirection);

            Assert.IsTrue(solved);
            Assert.AreEqual(
                upperLength,
                Vector3.Distance(root, elbow),
                0.00001f);
            Assert.AreEqual(
                lowerLength,
                Vector3.Distance(elbow, target),
                0.00001f);
            Assert.Greater(bendDirection.sqrMagnitude, 0.99f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void AnatomicalFrameMirrorsOutwardWithoutChangingForward()
        {
            ShoulderControllerRuntime.AnatomicalFrame rightFrame;
            ShoulderControllerRuntime.AnatomicalFrame leftFrame;

            Assert.IsTrue(
                ShoulderControllerRuntime.TryBuildAnatomicalFrame(
                    Vector3.up,
                    Vector3.right,
                    ShoulderControllerSide.Right,
                    false,
                    out rightFrame));
            Assert.IsTrue(
                ShoulderControllerRuntime.TryBuildAnatomicalFrame(
                    Vector3.up,
                    Vector3.right,
                    ShoulderControllerSide.Left,
                    false,
                    out leftFrame));

            Assert.Less(
                Vector3.Distance(rightFrame.Forward, Vector3.forward),
                0.00001f);
            Assert.Less(
                Vector3.Distance(leftFrame.Forward, Vector3.forward),
                0.00001f);
            Assert.Less(
                Vector3.Distance(rightFrame.Outward, Vector3.right),
                0.00001f);
            Assert.Less(
                Vector3.Distance(leftFrame.Outward, Vector3.left),
                0.00001f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void ReflectedMatrixIsDetectedWithoutReflectingControllerBasis()
        {
            Matrix4x4 reflected = Matrix4x4.Scale(
                new Vector3(-1f, 1f, 1f));
            Assert.IsTrue(
                ShoulderControllerRuntime.IsReflected(reflected));
            Assert.IsFalse(
                ShoulderControllerRuntime.IsReflected(Matrix4x4.identity));

            ShoulderControllerRuntime.AnatomicalFrame frame;
            Assert.IsTrue(
                ShoulderControllerRuntime.TryBuildAnatomicalFrame(
                    Vector3.up,
                    Vector3.left,
                    ShoulderControllerSide.Right,
                    true,
                    out frame));

            Assert.IsTrue(frame.Reflected);
            Assert.AreEqual(
                1f,
                Vector3.Dot(
                    Vector3.Cross(frame.Right, frame.Up),
                    frame.Forward),
                0.00001f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void RegistrationIsIdempotentAndEvaluationDoesNotAccumulate()
        {
            TestRig rig = CreateRig(false, false);
            Vector3 animatedHandPosition = rig.Hand.position;
            Quaternion animatedShoulderRotation = rig.Shoulder.rotation;

            rig.Asset.Initialize(rig.Data, null);
            rig.Asset.Initialize(rig.Data, null);

            Assert.AreEqual(1, rig.Runtime.RegistrationCount);

            rig.Runtime.EvaluateNow();
            Quaternion firstCorrectedRotation = rig.Shoulder.rotation;

            Assert.Greater(
                Quaternion.Angle(
                    animatedShoulderRotation,
                    firstCorrectedRotation),
                0.05f);
            Assert.Less(
                Vector3.Distance(animatedHandPosition, rig.Hand.position),
                rig.Asset.EndpointTolerance * 3f);

            rig.Runtime.EvaluateNow();

            Assert.Less(
                Quaternion.Angle(
                    firstCorrectedRotation,
                    rig.Shoulder.rotation),
                0.001f);
            Assert.Less(
                Vector3.Distance(animatedHandPosition, rig.Hand.position),
                rig.Asset.EndpointTolerance * 3f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void RootGlobalPositionFixupsDoNotChangeWorldSpaceResult()
        {
            TestRig plainRig = CreateRig(false, false);
            TestRig fixupRig = CreateRig(true, false);

            Vector3 plainTarget = plainRig.Hand.position;
            Vector3 fixupTarget = fixupRig.Hand.position;

            plainRig.Asset.Initialize(plainRig.Data, null);
            fixupRig.Asset.Initialize(fixupRig.Data, null);
            plainRig.Runtime.EvaluateNow();
            fixupRig.Runtime.EvaluateNow();

            Assert.Less(
                Vector3.Distance(plainTarget, plainRig.Hand.position),
                plainRig.Asset.EndpointTolerance * 3f);
            Assert.Less(
                Vector3.Distance(fixupTarget, fixupRig.Hand.position),
                fixupRig.Asset.EndpointTolerance * 3f);
            Assert.Less(
                Quaternion.Angle(
                    plainRig.Shoulder.rotation,
                    fixupRig.Shoulder.rotation),
                0.01f);
            Assert.Less(
                Quaternion.Angle(
                    plainRig.Arm.rotation,
                    fixupRig.Arm.rotation),
                0.01f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void ReflectedAncestorPreservesEndpointAndReportsReflection()
        {
            TestRig rig = CreateRig(true, true);
            Vector3 animatedHandPosition = rig.Hand.position;

            rig.Asset.Initialize(rig.Data, null);
            rig.Runtime.EvaluateNow();

            Assert.IsTrue(rig.Runtime.HasReflectedRegistration);
            Assert.Less(
                Vector3.Distance(animatedHandPosition, rig.Hand.position),
                rig.Asset.EndpointTolerance * 4f);
            Assert.IsFalse(float.IsNaN(rig.Shoulder.rotation.x));
            Assert.IsFalse(float.IsNaN(rig.Arm.rotation.x));
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void DownwardAnimatedShoulderIsLimitedAfterAnimation()
        {
            TestRig rig = CreateRig(false, false);
            rig.Asset.OverallEffect = 0f;
            rig.Asset.PreventShoulderPointingDown = true;
            rig.Asset.MaximumDownwardShoulderDegrees = 0f;

            rig.Shoulder.localRotation =
                Quaternion.AngleAxis(-10f, Vector3.forward);
            Vector3 animatedHandPosition = rig.Hand.position;
            Vector3 animatedShoulderDirection =
                (rig.Arm.position - rig.Shoulder.position).normalized;

            Assert.Less(
                Vector3.Dot(animatedShoulderDirection, Vector3.up),
                -0.1f);

            rig.Runtime.EvaluateNow();

            Vector3 limitedShoulderDirection =
                (rig.Arm.position - rig.Shoulder.position).normalized;
            Assert.GreaterOrEqual(
                Vector3.Dot(limitedShoulderDirection, Vector3.up),
                -0.00001f);
            Assert.Less(
                Vector3.Distance(animatedHandPosition, rig.Hand.position),
                rig.Asset.EndpointTolerance * 4f);
        }

        private TestRig CreateRig(bool useFixupHierarchy, bool reflected)
        {
            TestRig rig = new TestRig();
            rig.Avatar = new GameObject(
                useFixupHierarchy
                    ? "ShoulderFixupTestAvatar"
                    : "ShoulderPlainTestAvatar");

            rig.Root = CreateChild(rig.Avatar.transform, "Root");
            rig.Root.localRotation = useFixupHierarchy
                ? Quaternion.Euler(270f, 0f, 0f)
                : Quaternion.identity;
            rig.Root.localScale = reflected
                ? new Vector3(-1f, 1f, 1f)
                : Vector3.one;

            rig.Global = CreateChild(rig.Root, "Global");
            rig.Global.localRotation = useFixupHierarchy
                ? Quaternion.Euler(90f, 90f, 0f)
                : Quaternion.identity;

            rig.Position = CreateChild(rig.Global, "Position");
            if (!reflected)
            {
                // Give both test variants the same final skeleton space while
                // retaining different Root/Global local fixup rotations.
                rig.Position.position = rig.Avatar.transform.position;
                rig.Position.rotation = rig.Avatar.transform.rotation;
            }

            rig.Chest = CreateChild(rig.Position, "Chest");
            rig.Chest.localPosition = new Vector3(0f, 0.6f, 0f);

            rig.OppositeShoulder = CreateChild(
                rig.Chest,
                "LeftShoulder");
            rig.OppositeShoulder.localPosition =
                new Vector3(-0.2f, 0.15f, 0f);

            rig.Shoulder = CreateChild(rig.Chest, "RightShoulder");
            rig.Shoulder.localPosition = new Vector3(0.2f, 0.15f, 0f);

            rig.Arm = CreateChild(rig.Shoulder, "RightUpperArm");
            rig.Arm.localPosition = new Vector3(0.25f, 0f, 0f);

            rig.LowerArm = CreateChild(rig.Arm, "RightLowerArm");
            rig.LowerArm.localPosition = new Vector3(0.35f, 0f, 0f);
            rig.LowerArm.localRotation = Quaternion.Euler(0f, 0f, 45f);

            rig.Hand = CreateChild(rig.LowerArm, "RightHand");
            rig.Hand.localPosition = new Vector3(0.28f, 0f, 0f);

            rig.Data = rig.Avatar.AddComponent<UMAData>();
            rig.Data.umaRoot = rig.Root.gameObject;
            rig.Data.skeleton = new UMASkeleton(rig.Global);

            rig.Asset =
                ScriptableObject.CreateInstance<ShoulderControllerAnimator>();
            rig.Asset.name = "Shoulder Controller Test Asset";
            rig.Asset.ShoulderBoneName = rig.Shoulder.name;
            rig.Asset.ArmBoneName = rig.Arm.name;
            rig.Asset.LowerArmBoneName = rig.LowerArm.name;
            rig.Asset.HandBoneName = rig.Hand.name;
            rig.Asset.TorsoReferenceBoneName = rig.Chest.name;
            rig.Asset.OppositeShoulderBoneName =
                rig.OppositeShoulder.name;
            rig.Asset.Side = ShoulderControllerSide.Right;
            rig.Asset.EndpointMode =
                ShoulderEndpointMode.HandWhenAvailable;
            rig.Asset.DampingHalfLife = 0f;
            rig.Asset.EndpointTolerance = 0.0005f;
            rig.Asset.PosteriorRollEffect = 0f;

            rig.Asset.Initialize(rig.Data, null);
            rig.Runtime =
                rig.Avatar.GetComponent<ShoulderControllerRuntime>();
            rig.Runtime.AutomaticUpdate = false;

            _rigs.Add(rig);
            return rig;
        }

        private static Transform CreateChild(
            Transform parent,
            string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }
    }
}

#endif
