#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UMA;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.TestTools;

namespace UMA.Tests
{
    public sealed class DynamicExpressionPlayerRuntimeTests
    {
        private struct SetLocalRotationJob : IAnimationJob
        {
            public TransformStreamHandle bone;
            public Quaternion rotation;

            public void ProcessAnimation(AnimationStream stream)
            {
                bone.SetLocalRotation(stream, rotation);
            }

            public void ProcessRootMotion(AnimationStream stream) { }
        }

        [DefaultExecutionOrder(10000)]
        public sealed class LateFrameProbe : MonoBehaviour
        {
            public UMAData data;
            public string boneName;
            public float lastAngle;
            public int frameCount;

            private void LateUpdate()
            {
                lastAngle = BoneAngle(data, boneName);
                frameCount++;
            }
        }

        private readonly List<Object> _objects = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null)
                    Object.Destroy(_objects[i]);
            _objects.Clear();
            yield return null;
        }

        [UnityTest]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public IEnumerator FrameLaneRestoresOnDisableAndReappliesOnEnable()
        {
            const string boneName = "RuntimeExpressionBone";
            DynamicExpressionPlayer player = CreateRigPlayer(
                boneName, 24f, ExpressionJoint.Other,
                out UMAData data);
            LateFrameProbe probe =
                player.gameObject.AddComponent<LateFrameProbe>();
            probe.data = data;
            probe.boneName = boneName;
            player.SetExpression("runtime_expression", 1f);

            yield return null;
            yield return null;
            Assert.Greater(probe.frameCount, 0);
            Assert.AreEqual(24f, probe.lastAngle, 0.05f);

            player.enabled = false;
            Assert.AreEqual(0f, BoneAngle(data, boneName), 0.05f);

            player.enabled = true;
            yield return null;
            yield return null;
            Assert.AreEqual(24f, probe.lastAngle, 0.05f);
        }

        [UnityTest]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public IEnumerator LateRigLayersAfterActualAnimatorPlayable()
        {
            const string boneName = "AnimatedRuntimeJaw";
            DynamicExpressionPlayer player = CreateRigPlayer(
                boneName, 30f, ExpressionJoint.Jaw,
                out UMAData data);
            Animator animator = data.gameObject.AddComponent<Animator>();
            data.animator = animator;
            player.Rebind();
            player.SetExpression("runtime_expression", 1f);
            LateFrameProbe probe =
                player.gameObject.AddComponent<LateFrameProbe>();
            probe.data = data;
            probe.boneName = boneName;

            PlayableGraph graph = PlayableGraph.Create(
                "DynamicExpressionPlayerRuntimeTest");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            AnimationPlayableOutput output =
                AnimationPlayableOutput.Create(
                    graph, "Animation", animator);
            AnimationScriptPlayable playable =
                AnimationScriptPlayable.Create(graph,
                    new SetLocalRotationJob
                    {
                        bone = animator.BindStreamTransform(
                            data.skeleton.GetBoneTransform(boneName)),
                        rotation = Quaternion.Euler(0f, 0f, 12f)
                    });
            output.SetSourcePlayable(playable);
            graph.Play();

            try
            {
                player.overrideMecanimJaw = false;
                yield return null;
                yield return null;
                Assert.AreEqual(12f, probe.lastAngle, 0.1f);

                player.overrideMecanimJaw = true;
                yield return null;
                Assert.AreEqual(42f, probe.lastAngle, 0.15f);
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
            }
        }

        [UnityTest]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public IEnumerator HumanoidSaccadeFallbackYieldsToDNAEyeRoles()
        {
            GameObject avatar = CreateHumanoidAvatar(
                out Animator animator, out UMAData data);
            Assert.IsTrue(animator.isHuman);
            DynamicExpressionPlayer player =
                avatar.AddComponent<DynamicExpressionPlayer>();
            player.expressionGroupOverride = NewGroup();
            ConfigurePlayer(player);
            player.EnableSaccades = true;
            player.Rebind();

            yield return null;
            yield return null;
            Assert.IsTrue(player.AnimatorLookAtActive);
            Assert.AreEqual(player.EyesWeight,
                player.AnimatorLookAtEyesWeight, 0.0001f);
            Assert.Greater(Vector3.Dot(avatar.transform.forward,
                player.AnimatorLookAtPosition -
                avatar.transform.position), 0f);

            UMAExpressionDefinition horizontal =
                Definition("eye_x", NewDNA("eye_x", 0.5f));
            horizontal.roles = ExpressionRole.EyeHorizontal;
            UMAExpressionDefinition vertical =
                Definition("eye_y", NewDNA("eye_y", 0.5f));
            vertical.roles = ExpressionRole.EyeVertical;
            player.expressionGroupOverride =
                NewGroup(horizontal, vertical);
            player.Rebind();

            yield return null;
            Assert.IsFalse(player.AnimatorLookAtActive);
            Assert.IsTrue(player.TryGetExpressionIndex(
                horizontal.id, out int horizontalIndex));
            Assert.IsTrue(player.TryGetSourceValue(horizontalIndex,
                ExpressionSource.ProceduralGaze, out _, out bool active));
            Assert.IsTrue(active);

            GameObject target = Track(new GameObject("GazeTarget"));
            target.transform.position =
                avatar.transform.position +
                Vector3.up * 1.5f + Vector3.forward * 5f;
            player.LookAtTarget = target.transform;
            player.EnableLookAt = true;
            yield return null;

            Assert.IsTrue(player.AnimatorLookAtActive);
            Assert.AreEqual(0f,
                player.AnimatorLookAtEyesWeight, 0.0001f);
            Assert.AreEqual(target.transform.position,
                player.AnimatorLookAtPosition);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void SidedHorizontalGazeTurnsBothEyesTogether()
        {
            UMAExpressionDefinition left =
                Definition("left_eye_in_out", NewDNA("left_eye", 0.5f));
            left.roles = ExpressionRole.EyeHorizontalLeft;
            UMAExpressionDefinition right =
                Definition("right_eye_in_out", NewDNA("right_eye", 0.5f));
            right.roles = ExpressionRole.EyeHorizontalRight;
            GameObject avatar = Track(new GameObject("SaccadeAvatar"));
            DynamicExpressionPlayer player =
                avatar.AddComponent<DynamicExpressionPlayer>();
            player.expressionGroupOverride = NewGroup(left, right);
            ConfigurePlayer(player);
            player.Rebind();

            player.SetProceduralGazeDirection(Vector2.right);

            Assert.IsTrue(player.TryGetExpression(left.id,
                out float leftValue));
            Assert.IsTrue(player.TryGetExpression(right.id,
                out float rightValue));
            Assert.AreEqual(1f, leftValue, 0.0001f);
            Assert.AreEqual(0f, rightValue, 0.0001f);
        }

        [UnityTest]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public IEnumerator AutomaticRaceChangeRetainsOnlySharedStableIds()
        {
            UMAExpressionGroup first = NewGroup(
                Definition("shared", NewDNA("firstShared", 0.5f)),
                Definition("old", NewDNA("old", 0.4f)));
            UMAExpressionGroup second = NewGroup(
                Definition("shared", NewDNA("secondShared", 0.25f)),
                Definition("new", NewDNA("new", 0.6f)));
            RaceData firstRace = Track(
                ScriptableObject.CreateInstance<RaceData>());
            RaceData secondRace = Track(
                ScriptableObject.CreateInstance<RaceData>());
            firstRace.expressionGroup = first;
            secondRace.expressionGroup = second;

            GameObject avatar = Track(new GameObject("RaceChangeAvatar"));
            UMAData data = avatar.AddComponent<UMAData>();
            data.umaRecipe = new UMAData.UMARecipe
            {
                raceData = firstRace
            };
            DynamicExpressionPlayer player =
                avatar.AddComponent<DynamicExpressionPlayer>();
            ConfigurePlayer(player);
            player.Rebind();
            player.SetExpression("shared", 0.8f);
            player.SetExpression("old", 0.9f);

            data.umaRecipe.raceData = secondRace;
            yield return null;

            Assert.IsTrue(player.TryGetExpression("shared",
                out float retained));
            Assert.AreEqual(0.8f, retained, 0.0001f);
            Assert.IsFalse(player.TryGetExpression("old", out _));
            Assert.IsTrue(player.TryGetExpression("new",
                out float newValue));
            Assert.AreEqual(0.6f, newValue, 0.0001f);
        }

        private DynamicExpressionPlayer CreateRigPlayer(
            string boneName,
            float angle,
            ExpressionJoint joints,
            out UMAData data)
        {
            GameObject avatar = Track(new GameObject("RuntimeAvatar"));
            data = avatar.AddComponent<UMAData>();
            GameObject bone = new GameObject(boneName);
            bone.transform.SetParent(avatar.transform, false);
            data.skeleton = new UMASkeleton(avatar.transform);

            DNA dna = NewDNA("runtimeDNA", 0.5f);
            dna.effects.Add(new DNAEffect_BoneRotate
            {
                BoneName = boneName,
                RotationAxis = Vector3.forward,
                RotationAngle = angle
            });
            UMAExpressionGroup group = NewGroup(
                Definition("runtime_expression", dna, joints));
            DynamicExpressionPlayer player =
                avatar.AddComponent<DynamicExpressionPlayer>();
            player.expressionGroupOverride = group;
            ConfigurePlayer(player);
            player.Rebind();
            return player;
        }

        private GameObject CreateHumanoidAvatar(
            out Animator animator,
            out UMAData data)
        {
            GameObject root = Track(new GameObject("HumanoidAvatar"));
            List<Transform> skeleton = new List<Transform>();
            skeleton.Add(root.transform);
            Transform hips = CreateBone(root.transform, "Hips",
                new Vector3(0f, 1f, 0f), skeleton);
            Transform spine = CreateBone(hips, "Spine",
                new Vector3(0f, 0.2f, 0f), skeleton);
            Transform chest = CreateBone(spine, "Chest",
                new Vector3(0f, 0.2f, 0f), skeleton);
            Transform neck = CreateBone(chest, "Neck",
                new Vector3(0f, 0.2f, 0f), skeleton);
            Transform head = CreateBone(neck, "Head",
                new Vector3(0f, 0.2f, 0f), skeleton);
            Transform leftEye = CreateBone(head, "LeftEye",
                new Vector3(-0.03f, 0.05f, 0.08f), skeleton);
            Transform rightEye = CreateBone(head, "RightEye",
                new Vector3(0.03f, 0.05f, 0.08f), skeleton);
            Transform jaw = CreateBone(head, "Jaw",
                new Vector3(0f, -0.05f, 0.05f), skeleton);

            Transform leftUpperLeg = CreateBone(hips, "LeftUpperLeg",
                new Vector3(-0.1f, -0.35f, 0f), skeleton);
            Transform leftLowerLeg = CreateBone(
                leftUpperLeg, "LeftLowerLeg",
                new Vector3(0f, -0.4f, 0f), skeleton);
            Transform leftFoot = CreateBone(leftLowerLeg, "LeftFoot",
                new Vector3(0f, -0.35f, 0.08f), skeleton);
            Transform rightUpperLeg = CreateBone(hips, "RightUpperLeg",
                new Vector3(0.1f, -0.35f, 0f), skeleton);
            Transform rightLowerLeg = CreateBone(
                rightUpperLeg, "RightLowerLeg",
                new Vector3(0f, -0.4f, 0f), skeleton);
            Transform rightFoot = CreateBone(rightLowerLeg, "RightFoot",
                new Vector3(0f, -0.35f, 0.08f), skeleton);

            Transform leftShoulder = CreateBone(chest, "LeftShoulder",
                new Vector3(-0.15f, 0.12f, 0f), skeleton);
            Transform leftUpperArm = CreateBone(
                leftShoulder, "LeftUpperArm",
                new Vector3(-0.25f, 0f, 0f), skeleton);
            Transform leftLowerArm = CreateBone(
                leftUpperArm, "LeftLowerArm",
                new Vector3(-0.3f, 0f, 0f), skeleton);
            Transform leftHand = CreateBone(leftLowerArm, "LeftHand",
                new Vector3(-0.2f, 0f, 0f), skeleton);
            Transform rightShoulder = CreateBone(chest, "RightShoulder",
                new Vector3(0.15f, 0.12f, 0f), skeleton);
            Transform rightUpperArm = CreateBone(
                rightShoulder, "RightUpperArm",
                new Vector3(0.25f, 0f, 0f), skeleton);
            Transform rightLowerArm = CreateBone(
                rightUpperArm, "RightLowerArm",
                new Vector3(0.3f, 0f, 0f), skeleton);
            Transform rightHand = CreateBone(rightLowerArm, "RightHand",
                new Vector3(0.2f, 0f, 0f), skeleton);

            List<HumanBone> human = new List<HumanBone>();
            AddHumanBone(human, HumanBodyBones.Hips, hips);
            AddHumanBone(human, HumanBodyBones.Spine, spine);
            AddHumanBone(human, HumanBodyBones.Chest, chest);
            AddHumanBone(human, HumanBodyBones.Neck, neck);
            AddHumanBone(human, HumanBodyBones.Head, head);
            AddHumanBone(human, HumanBodyBones.LeftEye, leftEye);
            AddHumanBone(human, HumanBodyBones.RightEye, rightEye);
            AddHumanBone(human, HumanBodyBones.Jaw, jaw);
            AddHumanBone(human, HumanBodyBones.LeftUpperLeg, leftUpperLeg);
            AddHumanBone(human, HumanBodyBones.LeftLowerLeg, leftLowerLeg);
            AddHumanBone(human, HumanBodyBones.LeftFoot, leftFoot);
            AddHumanBone(human, HumanBodyBones.RightUpperLeg, rightUpperLeg);
            AddHumanBone(human, HumanBodyBones.RightLowerLeg, rightLowerLeg);
            AddHumanBone(human, HumanBodyBones.RightFoot, rightFoot);
            AddHumanBone(human, HumanBodyBones.LeftShoulder, leftShoulder);
            AddHumanBone(human, HumanBodyBones.LeftUpperArm, leftUpperArm);
            AddHumanBone(human, HumanBodyBones.LeftLowerArm, leftLowerArm);
            AddHumanBone(human, HumanBodyBones.LeftHand, leftHand);
            AddHumanBone(human, HumanBodyBones.RightShoulder, rightShoulder);
            AddHumanBone(human, HumanBodyBones.RightUpperArm, rightUpperArm);
            AddHumanBone(human, HumanBodyBones.RightLowerArm, rightLowerArm);
            AddHumanBone(human, HumanBodyBones.RightHand, rightHand);

            SkeletonBone[] skeletonBones =
                new SkeletonBone[skeleton.Count];
            for (int i = 0; i < skeleton.Count; i++)
                skeletonBones[i] = new SkeletonBone
                {
                    name = skeleton[i].name,
                    position = skeleton[i].localPosition,
                    rotation = skeleton[i].localRotation,
                    scale = skeleton[i].localScale
                };
            HumanDescription description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeletonBones,
                armStretch = 0.05f,
                legStretch = 0.05f,
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                feetSpacing = 0f,
                hasTranslationDoF = false
            };
            Avatar humanAvatar = Track(
                AvatarBuilder.BuildHumanAvatar(root, description));
            Assert.IsTrue(humanAvatar.isValid);
            Assert.IsTrue(humanAvatar.isHuman);

            animator = root.AddComponent<Animator>();
            animator.avatar = humanAvatar;
            data = root.AddComponent<UMAData>();
            data.animator = animator;
            data.skeleton = new UMASkeleton(root.transform);
            return root;
        }

        private static Transform CreateBone(
            Transform parent,
            string name,
            Vector3 localPosition,
            List<Transform> skeleton)
        {
            GameObject bone = new GameObject(name);
            bone.transform.SetParent(parent, false);
            bone.transform.localPosition = localPosition;
            skeleton.Add(bone.transform);
            return bone.transform;
        }

        private static void AddHumanBone(
            List<HumanBone> bones,
            HumanBodyBones humanBone,
            Transform target)
        {
            bones.Add(new HumanBone
            {
                boneName = target.name,
                humanName = HumanTrait.BoneName[(int)humanBone],
                limit = new HumanLimit { useDefaultValues = true }
            });
        }

        private static void ConfigurePlayer(
            DynamicExpressionPlayer player)
        {
            player.EnableBlinking = false;
            player.EnableSaccades = false;
            player.EnableLookAt = false;
            player.processDistance = 0f;
        }

        private static float BoneAngle(UMAData data, string boneName) =>
            Quaternion.Angle(Quaternion.identity,
                data.skeleton.GetRotation(
                    UMAUtils.StringToHash(boneName)));

        private DNA NewDNA(string name, float defaultValue)
        {
            DNA dna = Track(ScriptableObject.CreateInstance<DNA>());
            dna.name = name;
            dna.displayName = name;
            dna.defaultValue = defaultValue;
            return dna;
        }

        private UMAExpressionGroup NewGroup(
            params UMAExpressionDefinition[] definitions)
        {
            UMAExpressionGroup group =
                Track(ScriptableObject.CreateInstance<UMAExpressionGroup>());
            group.expressions.AddRange(definitions);
            return group;
        }

        private static UMAExpressionDefinition Definition(
            string id,
            DNA dna,
            ExpressionJoint joints = ExpressionJoint.Other) =>
            new UMAExpressionDefinition
            {
                id = id,
                displayName = id,
                dna = dna,
                affectedJoints = joints
            };

        private T Track<T>(T value) where T : Object
        {
            _objects.Add(value);
            return value;
        }
    }
}
#endif
