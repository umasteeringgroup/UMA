#if UNITY_EDITOR

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class PelvisControllerAnimatorTests
    {
        private sealed class TestFootIKProvider :
            MonoBehaviour,
            IUMAFootIKProvider
        {
            public UMAFootIKState Left;
            public UMAFootIKState Right;

            public bool TryGetFootIKState(
                PelvisLegSide side,
                out UMAFootIKState state)
            {
                state = side == PelvisLegSide.Left
                    ? Left
                    : Right;
                return state.Valid;
            }
        }

        private sealed class TestRig
        {
            public GameObject Avatar;
            public UMAData Data;
            public Transform Root;
            public Transform Global;
            public Transform Position;
            public Transform Hips;
            public Transform Spine;
            public Transform Chest;
            public Transform LeftUpper;
            public Transform LeftLower;
            public Transform LeftFoot;
            public Transform LeftToe;
            public Transform RightUpper;
            public Transform RightLower;
            public Transform RightFoot;
            public Transform RightToe;
            public PelvisControllerAnimator Asset;
            public PelvisControllerRuntime Runtime;
            public TestFootIKProvider Provider;
        }

        private readonly List<TestRig> _rigs =
            new List<TestRig>();

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
        public void TwoBoneJointPreservesLengthsAndMinimumFlexion()
        {
            Vector3 root = Vector3.zero;
            Vector3 target = new Vector3(0.1f, -0.8f, 0.15f);
            const float upperLength = 0.55f;
            const float lowerLength = 0.5f;

            Vector3 joint;
            Vector3 bendDirection;
            bool solved =
                PelvisControllerRuntime.TryCalculateTwoBoneJoint(
                    root,
                    target,
                    new Vector3(0f, -0.4f, 0.35f),
                    Vector3.forward,
                    upperLength,
                    lowerLength,
                    5f,
                    out joint,
                    out bendDirection);

            Assert.IsTrue(solved);
            Assert.AreEqual(
                upperLength,
                Vector3.Distance(root, joint),
                0.00001f);
            Assert.AreEqual(
                lowerLength,
                Vector3.Distance(joint, target),
                0.00001f);
            Assert.Greater(bendDirection.sqrMagnitude, 0.99f);

            float maximumReach =
                PelvisControllerRuntime.CalculateMaximumReach(
                    upperLength,
                    lowerLength,
                    5f);
            Assert.Less(
                maximumReach,
                upperLength + lowerLength);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void AnatomicalFrameRemainsRightHandedWhenReflected()
        {
            PelvisControllerRuntime.AnatomicalFrame frame;
            Assert.IsTrue(
                PelvisControllerRuntime.TryBuildAnatomicalFrame(
                    Vector3.up,
                    Vector3.left,
                    true,
                    out frame));

            Assert.IsTrue(frame.Reflected);
            Assert.AreEqual(
                1f,
                Vector3.Dot(
                    Vector3.Cross(frame.Right, frame.Up),
                    frame.Forward),
                0.00001f);
            Assert.IsTrue(
                PelvisControllerRuntime.IsReflected(
                    Matrix4x4.Scale(
                        new Vector3(-1f, 1f, 1f))));
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void FootIKDefaultsOffAndAnimatedFeetArePreserved()
        {
            TestRig rig = CreateRig(false, false, false);
            Vector3 leftTarget = rig.LeftFoot.position;
            Vector3 rightTarget = rig.RightFoot.position;
            Quaternion chestRotation = rig.Chest.rotation;
            Quaternion hipsRotation = rig.Hips.rotation;

            Assert.AreEqual(
                PelvisFootIKMode.None,
                rig.Asset.FootIKMode);

            rig.Runtime.EvaluateNow();

            Assert.Greater(
                Quaternion.Angle(
                    hipsRotation,
                    rig.Hips.rotation),
                0.05f);
            Assert.Less(
                Vector3.Distance(
                    leftTarget,
                    rig.LeftFoot.position),
                rig.Asset.EndpointTolerance * 4f);
            Assert.Less(
                Vector3.Distance(
                    rightTarget,
                    rig.RightFoot.position),
                rig.Asset.EndpointTolerance * 4f);
            Assert.Less(
                Quaternion.Angle(
                    chestRotation,
                    rig.Chest.rotation),
                0.01f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void RegistrationAndEvaluationDoNotAccumulate()
        {
            TestRig rig = CreateRig(false, false, false);
            rig.Asset.Initialize(rig.Data, null);
            rig.Asset.Initialize(rig.Data, null);
            Assert.AreEqual(1, rig.Runtime.RegistrationCount);

            rig.Runtime.EvaluateNow();
            Quaternion firstHips = rig.Hips.rotation;
            Vector3 firstLeftFoot = rig.LeftFoot.position;
            Vector3 firstRightFoot = rig.RightFoot.position;

            rig.Runtime.EvaluateNow();

            Assert.Less(
                Quaternion.Angle(firstHips, rig.Hips.rotation),
                0.001f);
            Assert.Less(
                Vector3.Distance(
                    firstLeftFoot,
                    rig.LeftFoot.position),
                0.00001f);
            Assert.Less(
                Vector3.Distance(
                    firstRightFoot,
                    rig.RightFoot.position),
                0.00001f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void RootGlobalPositionFixupsProduceSameWorldSolve()
        {
            TestRig plain = CreateRig(false, false, false);
            TestRig fixup = CreateRig(true, false, false);
            Vector3 plainLeft = plain.LeftFoot.position;
            Vector3 plainRight = plain.RightFoot.position;
            Vector3 fixupLeft = fixup.LeftFoot.position;
            Vector3 fixupRight = fixup.RightFoot.position;

            plain.Runtime.EvaluateNow();
            fixup.Runtime.EvaluateNow();

            Assert.Less(
                Vector3.Distance(plainLeft, plain.LeftFoot.position),
                plain.Asset.EndpointTolerance * 4f);
            Assert.Less(
                Vector3.Distance(plainRight, plain.RightFoot.position),
                plain.Asset.EndpointTolerance * 4f);
            Assert.Less(
                Vector3.Distance(fixupLeft, fixup.LeftFoot.position),
                fixup.Asset.EndpointTolerance * 4f);
            Assert.Less(
                Vector3.Distance(fixupRight, fixup.RightFoot.position),
                fixup.Asset.EndpointTolerance * 4f);
            Assert.Less(
                Quaternion.Angle(
                    plain.Hips.rotation,
                    fixup.Hips.rotation),
                0.02f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void ReflectedAncestorPreservesBothFeet()
        {
            TestRig rig = CreateRig(true, true, false);
            Vector3 leftTarget = rig.LeftFoot.position;
            Vector3 rightTarget = rig.RightFoot.position;

            rig.Runtime.EvaluateNow();

            Assert.IsTrue(rig.Runtime.HasReflectedRegistration);
            Assert.Less(
                Vector3.Distance(
                    leftTarget,
                    rig.LeftFoot.position),
                rig.Asset.EndpointTolerance * 5f);
            Assert.Less(
                Vector3.Distance(
                    rightTarget,
                    rig.RightFoot.position),
                rig.Asset.EndpointTolerance * 5f);
            Assert.IsFalse(float.IsNaN(rig.Hips.rotation.x));
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void GoalProviderMakesPlantedFootAuthoritative()
        {
            TestRig rig = CreateRig(false, false, true);
            Vector3 leftGoal =
                rig.LeftFoot.position + Vector3.up * 0.015f;
            rig.Provider.Left = CreateFootState(
                leftGoal,
                rig.LeftFoot.rotation,
                1f,
                1f);
            rig.Provider.Right = CreateFootState(
                rig.RightFoot.position,
                rig.RightFoot.rotation,
                0f,
                0f);
            rig.Asset.FootIKMode =
                PelvisFootIKMode.GoalProvider;
            rig.Asset.ApplyProviderGoalsInAnimatorIK = false;
            rig.Asset.ObliquityEffect = 0.5f;
            rig.Asset.Initialize(rig.Data, null);

            rig.Runtime.EvaluateNow();

            Assert.Less(
                Vector3.Distance(
                    leftGoal,
                    rig.LeftFoot.position),
                rig.Asset.EndpointTolerance * 5f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void ProviderPositionWeightDoesNotImplySupportWhileAirborne()
        {
            TestRig rig = CreateRig(false, false, true);
            rig.Provider.Left = CreateFootState(
                rig.LeftFoot.position,
                rig.LeftFoot.rotation,
                1f,
                0f);
            rig.Provider.Right = CreateFootState(
                rig.RightFoot.position,
                rig.RightFoot.rotation,
                0f,
                0f);
            rig.Asset.FootIKMode =
                PelvisFootIKMode.GoalProvider;
            rig.Asset.ApplyProviderGoalsInAnimatorIK = false;
            rig.Asset.ObliquityEffect = 1f;
            rig.Asset.PelvicTiltEffect = 0f;
            rig.Asset.Initialize(rig.Data, null);
            Quaternion hipsRotation = rig.Hips.rotation;

            rig.Runtime.EvaluateNow();

            Assert.Less(
                Quaternion.Angle(
                    hipsRotation,
                    rig.Hips.rotation),
                0.001f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void AutomaticWithoutActiveFootIKFallsBackToAnimatedFeet()
        {
            TestRig rig = CreateRig(false, false, false);
            rig.Asset.FootIKMode = PelvisFootIKMode.Automatic;
            rig.Asset.Initialize(rig.Data, null);
            Vector3 leftTarget = rig.LeftFoot.position;
            Vector3 rightTarget = rig.RightFoot.position;

            rig.Runtime.EvaluateNow();

            Assert.Less(
                Vector3.Distance(
                    leftTarget,
                    rig.LeftFoot.position),
                rig.Asset.EndpointTolerance * 4f);
            Assert.Less(
                Vector3.Distance(
                    rightTarget,
                    rig.RightFoot.position),
                rig.Asset.EndpointTolerance * 4f);
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void SeparateAnimatorObjectReceivesIKBridge()
        {
            TestRig rig = CreateRig(false, false, false);
            Transform animatorObject = CreateChild(
                rig.Avatar.transform,
                "AnimatorObject");
            rig.Data.animator =
                animatorObject.gameObject.AddComponent<Animator>();

            rig.Asset.Initialize(rig.Data, null);

            Assert.IsNotNull(
                animatorObject.GetComponent<
                    PelvisControllerIKBridge>());
        }

        private TestRig CreateRig(
            bool useFixupHierarchy,
            bool reflected,
            bool addProvider)
        {
            TestRig rig = new TestRig();
            rig.Avatar = new GameObject(
                useFixupHierarchy
                    ? "PelvisFixupTestAvatar"
                    : "PelvisPlainTestAvatar");

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
                rig.Position.position = rig.Avatar.transform.position;
                rig.Position.rotation = rig.Avatar.transform.rotation;
            }

            rig.Hips = CreateChild(rig.Position, "Hips");
            rig.Hips.localPosition = new Vector3(0f, 1f, 0f);

            rig.Spine = CreateChild(rig.Hips, "Spine");
            rig.Spine.localPosition = new Vector3(0f, 0.35f, 0f);
            rig.Chest = CreateChild(rig.Spine, "Chest");
            rig.Chest.localPosition = new Vector3(0f, 0.35f, 0f);

            rig.LeftUpper = CreateChild(
                rig.Hips,
                "LeftUpperLeg");
            rig.LeftUpper.localPosition =
                new Vector3(-0.16f, -0.05f, 0f);
            rig.LeftUpper.localRotation =
                Quaternion.Euler(-35f, 0f, 0f);
            rig.LeftLower = CreateChild(
                rig.LeftUpper,
                "LeftLowerLeg");
            rig.LeftLower.localPosition =
                new Vector3(0f, -0.46f, 0f);
            rig.LeftLower.localRotation =
                Quaternion.Euler(50f, 0f, 0f);
            rig.LeftFoot = CreateChild(
                rig.LeftLower,
                "LeftFoot");
            rig.LeftFoot.localPosition =
                new Vector3(0f, -0.43f, 0f);
            rig.LeftToe = CreateChild(
                rig.LeftFoot,
                "LeftToe");
            rig.LeftToe.localPosition =
                new Vector3(0f, 0f, 0.12f);

            rig.RightUpper = CreateChild(
                rig.Hips,
                "RightUpperLeg");
            rig.RightUpper.localPosition =
                new Vector3(0.16f, -0.05f, 0f);
            rig.RightUpper.localRotation =
                Quaternion.Euler(35f, 0f, 0f);
            rig.RightLower = CreateChild(
                rig.RightUpper,
                "RightLowerLeg");
            rig.RightLower.localPosition =
                new Vector3(0f, -0.46f, 0f);
            rig.RightLower.localRotation =
                Quaternion.Euler(-50f, 0f, 0f);
            rig.RightFoot = CreateChild(
                rig.RightLower,
                "RightFoot");
            rig.RightFoot.localPosition =
                new Vector3(0f, -0.43f, 0f);
            rig.RightToe = CreateChild(
                rig.RightFoot,
                "RightToe");
            rig.RightToe.localPosition =
                new Vector3(0f, 0f, 0.12f);

            rig.Data = rig.Avatar.AddComponent<UMAData>();
            rig.Data.umaRoot = rig.Root.gameObject;
            rig.Data.skeleton = new UMASkeleton(rig.Global);

            if (addProvider)
            {
                rig.Provider =
                    rig.Avatar.AddComponent<TestFootIKProvider>();
            }

            rig.Asset =
                ScriptableObject.CreateInstance<
                    PelvisControllerAnimator>();
            rig.Asset.name = "Pelvis Controller Test Asset";
            rig.Asset.HipsBoneName = rig.Hips.name;
            rig.Asset.LeftUpperLegBoneName =
                rig.LeftUpper.name;
            rig.Asset.RightUpperLegBoneName =
                rig.RightUpper.name;
            rig.Asset.LeftLowerLegBoneName =
                rig.LeftLower.name;
            rig.Asset.RightLowerLegBoneName =
                rig.RightLower.name;
            rig.Asset.LeftFootBoneName = rig.LeftFoot.name;
            rig.Asset.RightFootBoneName = rig.RightFoot.name;
            rig.Asset.LeftToeBoneName = rig.LeftToe.name;
            rig.Asset.RightToeBoneName = rig.RightToe.name;
            rig.Asset.SpineBoneNames = new[]
            {
                rig.Spine.name,
                rig.Chest.name
            };
            rig.Asset.UpperBodyReferenceBoneName =
                rig.Chest.name;
            rig.Asset.FootIKMode = PelvisFootIKMode.None;
            rig.Asset.AnimatedEndpointPreservation = 1f;
            rig.Asset.StrideRotationEffect = 1f;
            rig.Asset.ObliquityEffect = 0f;
            rig.Asset.PelvicTiltEffect = 0f;
            rig.Asset.MaximumStrideRotationDegrees = 6f;
            rig.Asset.TorsoYawFollow = 0f;
            rig.Asset.DampingHalfLife = 0f;
            rig.Asset.EndpointTolerance = 0.0005f;

            rig.Asset.Initialize(rig.Data, null);
            rig.Runtime =
                rig.Avatar.GetComponent<PelvisControllerRuntime>();
            rig.Runtime.AutomaticUpdate = false;

            _rigs.Add(rig);
            return rig;
        }

        private static UMAFootIKState CreateFootState(
            Vector3 position,
            Quaternion rotation,
            float positionWeight,
            float plantedWeight)
        {
            return new UMAFootIKState
            {
                Valid = true,
                Position = position,
                Rotation = rotation,
                PositionWeight = positionWeight,
                RotationWeight = positionWeight,
                PlantedWeight = plantedWeight,
                GroundNormal = Vector3.up
            };
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
