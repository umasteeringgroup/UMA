using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.Examples;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Examples.Tests
{
    internal sealed class WalkerFixture : IDisposable
    {
        private readonly List<Object> objects = new List<Object>();
        public readonly GameObject root;
        public readonly RandomCharacterWalker walker;
        public Animator animator;

        public WalkerFixture()
        {
            root = new GameObject("Walker regression fixture");
            root.SetActive(false);
            objects.Add(root);
            walker = root.AddComponent<RandomCharacterWalker>();
            walker.headingNoise = 0f;
            walker.randomHeadingVariation = 0f;
            walker.pauseInterval = new Vector2(1000f, 1000f);
            walker.headingChangeInterval = new Vector2(1000f, 1000f);
            walker.maximumSpawnDistance = 100f;
            walker.movementStartGraceTime = 0f;
            walker.animationDamping = 0f;
            walker.progressCheckInterval = 1f;
        }

        public void InitializeForUnitTest()
        {
            root.SetActive(true);
            Call("Start");
            Call("UpdateAnimation", walker.walkingAnimationSpeed);
            Call("ResetMovementProgress", 0f);
        }

        public Animator AddAnimator(bool rootMotion = false)
        {
            var bone = new GameObject("Motion");
            bone.transform.SetParent(root.transform, false);
            var controller = new AnimatorController();
            objects.Add(controller);
            controller.AddLayer("Base");
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Direction", AnimatorControllerParameterType.Float);
            var idle = new AnimationClip { name = "Idle" };
            var walk = new AnimationClip { name = "Walk" };
            objects.Add(idle);
            objects.Add(walk);
            idle.SetCurve("Motion", typeof(Transform), "localRotation.w", AnimationCurve.Constant(0f, 1f, 1f));
            walk.SetCurve("Motion", typeof(Transform), "localRotation.w", AnimationCurve.Constant(0f, 10f, 1f));
            if (rootMotion)
            {
                walk.SetCurve("", typeof(Animator), "MotionT.z", AnimationCurve.Linear(0f, 0f, 10f, 10f));
                walk.SetCurve("", typeof(Animator), "MotionQ.w", AnimationCurve.Constant(0f, 10f, 1f));
            }
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            objects.Add(machine);
            AnimatorState idleState = machine.AddState("Idle");
            AnimatorState walkState = machine.AddState("Walk");
            objects.Add(idleState);
            objects.Add(walkState);
            idleState.motion = idle;
            walkState.motion = walk;
            machine.defaultState = idleState;
            AnimatorStateTransition begin = idleState.AddTransition(walkState);
            begin.hasExitTime = false;
            begin.duration = 0f;
            begin.AddCondition(AnimatorConditionMode.Greater, 0.01f, "Speed");
            AnimatorStateTransition stop = walkState.AddTransition(idleState);
            stop.hasExitTime = false;
            stop.duration = 0f;
            stop.AddCondition(AnimatorConditionMode.Less, 0.01f, "Speed");
            objects.Add(begin);
            objects.Add(stop);
            animator = root.AddComponent<Animator>();
            if (rootMotion)
            {
                Avatar avatar = AvatarBuilder.BuildGenericAvatar(root, "Motion");
                objects.Add(avatar);
                animator.avatar = avatar;
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        public object Call(string name, params object[] args)
        {
            MethodInfo method = typeof(RandomCharacterWalker).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(walker, args);
        }

        public void Set(string name, object value)
        {
            FieldInfo field = typeof(RandomCharacterWalker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(walker, value);
        }

        public T Get<T>(string name)
        {
            return (T)typeof(RandomCharacterWalker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(walker);
        }

        public void Window(float now, float requested, float actual)
        {
            Call("RecordMovementProgress", Vector3.forward * requested, Vector3.forward * actual, now);
            Call("CheckForStall", now);
        }

        public void Dispose()
        {
            // Disable the live component before destroying its controller/animation objects.
            if (root != null) root.SetActive(false);
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }
    }

    public sealed class RandomCharacterWalkerTests
    {
        private WalkerFixture fixture;
        private RandomCharacterWalker Walker => fixture.walker;

        [SetUp]
        public void SetUp()
        {
            fixture = new WalkerFixture();
            fixture.InitializeForUnitTest();
        }

        [TearDown]
        public void TearDown() => fixture.Dispose();

        [TestCase(0.05f)]
        [TestCase(0.1f)]
        [TestCase(0.75f)]
        [TestCase(2f)]
        public void AutoClassifiesAuthoredRootMotionIndependentlyOfPlaybackSpeed(float playback)
        {
            Walker.animationPlaybackSpeed = playback;
            Vector3 authoredDelta = Vector3.forward * (0.6f * playback * 0.02f);
            var resolved = (Vector3)fixture.Call("ResolveLocomotionDelta", authoredDelta, 0.02f);
            Assert.That(resolved.z, Is.EqualTo(authoredDelta.z).Within(0.000001f));
        }

        [Test]
        public void NewWalkersDefaultToNormalAnimationPlayback()
        {
            Assert.That(Walker.animationPlaybackSpeed, Is.EqualTo(1f));
        }

        [TestCase(0.05f)]
        [TestCase(0.75f)]
        [TestCase(2f)]
        public void InPlaceFallbackScalesLinearlyWithPlayback(float playback)
        {
            Walker.animationPlaybackSpeed = playback;
            var resolved = (Vector3)fixture.Call("ResolveLocomotionDelta", Vector3.zero, 0.02f);
            Assert.That(resolved.z, Is.EqualTo(1.4f * playback * 0.02f).Within(0.000001f));
        }

        [Test]
        public void RootMotionModeNeverReplacesSlowAuthoredMovementOrIdleWithFallback()
        {
            Walker.locomotionSource = RandomCharacterWalker.LocomotionSource.RootMotion;
            Assert.That((Vector3)fixture.Call("ResolveLocomotionDelta", Vector3.zero, 0.02f), Is.EqualTo(Vector3.zero));
            Vector3 delta = Vector3.forward * 0.00001f;
            Assert.That((Vector3)fixture.Call("ResolveLocomotionDelta", delta, 0.02f), Is.EqualTo(delta));
        }

        [Test]
        public void ExplicitInPlaceModeIgnoresAuthoredTravel()
        {
            Walker.locomotionSource = RandomCharacterWalker.LocomotionSource.InPlace;
            var delta = (Vector3)fixture.Call("ResolveLocomotionDelta", Vector3.right, 0.02f);
            Assert.That(delta.x, Is.Zero);
            Assert.That(delta.z, Is.EqualTo(1.4f * Walker.animationPlaybackSpeed * 0.02f).Within(0.000001f));
        }

        [Test]
        public void ZeroDeltaTimeProducesNoTranslation()
        {
            Assert.That((Vector3)fixture.Call("ResolveLocomotionDelta", Vector3.forward, 0f), Is.EqualTo(Vector3.zero));
        }

        [TestCase(0.07f)]
        [TestCase(0.001f)]
        public void SlowSuccessfulWalkingIsNotConsideredBlocked(float distance)
        {
            for (int i = 1; i < 20; i++) fixture.Window(i, distance, distance);
            Assert.That(Walker.IsMovementBlocked, Is.False);
            Assert.That(fixture.Get<int>("stalledCheckCount"), Is.Zero);
        }

        [Test]
        public void SustainedBlockingIdlesThenRetriesWithoutRequiringMeasuredMovement()
        {
            fixture.Window(1f, 0.1f, 0f);
            Assert.That(Walker.IsMovementBlocked, Is.False);
            fixture.Window(2f, 0.1f, 0f);
            Assert.That(Walker.IsMovementBlocked, Is.True);
            fixture.Call("CheckForStall", 2.5f);
            Assert.That(Walker.IsMovementBlocked, Is.True);
            fixture.Call("CheckForStall", 3f);
            Assert.That(Walker.IsMovementBlocked, Is.False);
            Assert.That(fixture.Get<bool>("walkingRequested"), Is.True);
            fixture.Window(4f, 0.1f, 0.1f);
            Assert.That(Walker.IsMovementBlocked, Is.False);
        }

        [TestCase(0.1f, 0.015f, 0.03f)]
        [TestCase(1f, 0.04f, 0.08f)]
        public void HysteresisNeitherAddsNorClearsChecksInsideTheThresholdBand(
            float requested, float intermediate, float resumed)
        {
            fixture.Window(1f, requested, 0f);
            fixture.Window(2f, requested, intermediate);
            Assert.That(fixture.Get<int>("stalledCheckCount"), Is.EqualTo(1));
            Assert.That(Walker.IsMovementBlocked, Is.False);
            fixture.Window(3f, requested, resumed);
            Assert.That(fixture.Get<int>("stalledCheckCount"), Is.Zero);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void ProgressChecksAreStableAcrossAnimationSampleRates(int framesPerSecond)
        {
            float dt = 1f / framesPerSecond;
            for (int frame = 1; frame <= framesPerSecond * 3; frame++)
            {
                fixture.Call("RecordMovementProgress", Vector3.forward * (0.07f * dt),
                    Vector3.forward * (0.07f * dt), frame * dt);
                fixture.Call("CheckForStall", frame * dt);
            }
            Assert.That(Walker.IsMovementBlocked, Is.False);
            fixture.Call("ResetMovementProgress", 3f);
            for (int frame = 1; frame <= framesPerSecond * 2 + 2; frame++)
            {
                fixture.Call("RecordMovementProgress", Vector3.forward * dt, Vector3.zero, 3f + frame * dt);
                fixture.Call("CheckForStall", 3f + frame * dt);
            }
            Assert.That(Walker.IsMovementBlocked, Is.True);
        }

        [Test]
        public void StartupGraceDoesNotAccumulateBlockedSamples()
        {
            Walker.movementStartGraceTime = 0.5f;
            fixture.Call("ResetMovementProgress", 0f);
            fixture.Window(0.2f, 1f, 0f);
            Assert.That(fixture.Get<float>("requestedProgressDistance"), Is.Zero);
            fixture.Call("CheckForStall", 1.5f);
            Assert.That(fixture.Get<int>("stalledCheckCount"), Is.Zero);
        }

        [Test]
        public void PauseResumeAndEnableResetBlockageAndPhysicsSampling()
        {
            fixture.Window(1f, 1f, 0f);
            fixture.Window(2f, 1f, 0f);
            fixture.Call("UpdateAnimation", 0f);
            Assert.That(Walker.IsMovementBlocked, Is.False);
            fixture.Call("UpdateAnimation", Walker.walkingAnimationSpeed);
            Assert.That(fixture.Get<bool>("walkingRequested"), Is.True);
            fixture.Set("physicsSamplePending", true);
            fixture.Set("initialized", true);
            Walker.enabled = false;
            fixture.Call("OnDisable"); // Edit Mode does not dispatch ordinary MonoBehaviour lifecycle messages.
            fixture.root.transform.position += Vector3.right * 30f;
            Walker.enabled = true;
            fixture.Call("OnEnable");
            Assert.That(fixture.Get<bool>("physicsSamplePending"), Is.False);
            Assert.That(fixture.Get<float>("requestedProgressDistance"), Is.Zero);
        }

        [Test]
        public void RagdollRecoveryClearsOldProgressAndAllowsWalking()
        {
            fixture.Window(1f, 1f, 0f);
            Walker.SetShooterRagdolled(true);
            Assert.That(Walker.enabled, Is.False);
            fixture.root.transform.position += Vector3.right * 20f;
            Walker.SetShooterRagdolled(false);
            fixture.Call("UpdateAnimation", Walker.walkingAnimationSpeed);
            Assert.That(Walker.enabled, Is.True);
            Assert.That(fixture.Get<bool>("walkingRequested"), Is.True);
            Assert.That(Walker.IsMovementBlocked, Is.False);
            Assert.That(fixture.Get<int>("stalledCheckCount"), Is.Zero);
        }

        [Test]
        public void TeleportIsNotCountedAsLocomotion()
        {
            fixture.Window(1f, 1f, 0f);
            fixture.Call("RecordMovementProgress", Vector3.forward * 0.02f, Vector3.forward * 50f, 1.1f);
            Assert.That(fixture.Get<float>("actualProgressDistance"), Is.Zero);
            Assert.That(fixture.Get<int>("stalledCheckCount"), Is.Zero);
        }

        [Test]
        public void SidewaysAndVerticalDisplacementCannotMaskAForwardBlockage()
        {
            for (int i = 1; i <= 2; i++)
            {
                fixture.Call("RecordMovementProgress", Vector3.forward * 0.02f,
                    Vector3.right * 0.1f + Vector3.down, (float)i);
                fixture.Call("CheckForStall", (float)i);
            }
            Assert.That(Walker.IsMovementBlocked, Is.True);
        }

        [Test]
        public void CrowdSeparationIsRemovedFromPendingPhysicsSample()
        {
            Rigidbody body = fixture.root.AddComponent<Rigidbody>();
            body.useGravity = false;
            fixture.Call("RefreshMovementBody");
            fixture.Set("physicsSamplePending", true);
            fixture.Set("physicsSamplePosition", body.position);
            fixture.Call("ApplyPositionWithinSpawnRadius", body.position + Vector3.forward * 0.1f);
            Assert.That(fixture.Get<Vector3>("physicsSamplePosition"), Is.EqualTo(body.position));
            Assert.That(fixture.Get<float>("actualProgressDistance"), Is.Zero);
        }

        [Test]
        public void CombatRetryKeepsTargetDirectionAndDoesNotRequireProgress()
        {
            fixture.Set("isPursuingCombatTarget", true);
            fixture.Set("desiredDirection", Vector3.right);
            fixture.Window(1f, 1f, 0f);
            fixture.Window(2f, 1f, 0f);
            fixture.Call("CheckForStall", 3f);
            Assert.That(Walker.IsMovementBlocked, Is.False);
            Assert.That(fixture.Get<Vector3>("desiredDirection"), Is.EqualTo(Vector3.right));
        }

        [Test]
        public void IdleZerosDirectionAndWalkingCanStartWithZeroObservedProgress()
        {
            fixture.root.SetActive(false);
            Animator animator = fixture.AddAnimator();
            fixture.root.SetActive(true);
            animator.Rebind();
            animator.Update(0f);
            fixture.Call("RefreshAnimator");
            fixture.Set("desiredDirection", Vector3.right);
            fixture.Call("UpdateAnimation", Walker.walkingAnimationSpeed);
            Assert.That(animator.GetFloat("Speed"), Is.EqualTo(Walker.walkingAnimationSpeed).Within(0.0001f));
            fixture.Call("UpdateAnimation", 0f);
            Assert.That(animator.GetFloat("Speed"), Is.Zero);
            Assert.That(animator.GetFloat("Direction"), Is.Zero);
        }
    }

    public sealed class RandomCharacterWalkerPlayModeTests
    {
        [UnityTest]
        public IEnumerator PhysicsWalkerIdlesAtWallAndResumesAfterItIsRemoved()
        {
            yield return new EnterPlayMode();
            using (var fixture = new WalkerFixture())
            {
                var floor = new GameObject("Walker test floor");
                var wall = new GameObject("Walker test wall");
                var target = new GameObject("Walker test pursuit target");
                try
                {
                    floor.AddComponent<BoxCollider>().size = new Vector3(100f, 1f, 100f);
                    floor.transform.position = new Vector3(0f, -0.5f, 0f);
                    wall.AddComponent<BoxCollider>().size = new Vector3(10f, 3f, 0.2f);
                    wall.transform.position = new Vector3(0f, 1.5f, 1f);
                    target.transform.position = new Vector3(0f, 1f, 30f);
                    fixture.root.transform.position = Vector3.up;
                    CapsuleCollider capsule = fixture.root.AddComponent<CapsuleCollider>();
                    capsule.height = 2f;
                    capsule.radius = 0.25f;
                    Rigidbody body = fixture.root.AddComponent<Rigidbody>();
                    body.constraints = RigidbodyConstraints.FreezeRotation;
                    fixture.AddAnimator();
                    fixture.walker.progressCheckInterval = 0.25f;
                    fixture.walker.stalledChecksBeforeRecovery = 2;
                    fixture.walker.blockedRetryInterval = 10f;
                    fixture.walker.movementStartGraceTime = 0.1f;
                    fixture.root.SetActive(true);
                    yield return null;
                    fixture.walker.ApplyShot(target.transform, false);
                    float deadline = Time.time + 5f;
                    while (!fixture.walker.IsMovementBlocked && Time.time < deadline)
                        yield return new WaitForFixedUpdate();
                    Assert.That(fixture.walker.IsMovementBlocked, Is.True, "The collision solver should block forward travel.");
                    yield return null;
                    yield return new WaitForFixedUpdate();
                    Assert.That(fixture.animator.GetFloat("Speed"), Is.Zero);
                    Assert.That(Mathf.Abs(body.linearVelocity.z), Is.LessThan(0.01f));
                    float blockedZ = body.position.z;
                    Object.Destroy(wall);
                    fixture.Set("blockedRetryAt", Time.time);
                    deadline = Time.time + 3f;
                    while (body.position.z < blockedZ + 0.25f && Time.time < deadline)
                        yield return new WaitForFixedUpdate();
                    Assert.That(body.position.z, Is.GreaterThan(blockedZ + 0.25f));
                    Assert.That(fixture.walker.IsMovementBlocked, Is.False);
                    Assert.That(body.position.y, Is.EqualTo(1f).Within(0.1f), "Gravity/floor contact must remain controlled by physics.");

                    target.transform.position = body.position;
                    yield return null;
                    yield return new WaitForFixedUpdate();
                    Assert.That(fixture.animator.GetFloat("Speed"), Is.Zero, "Pursuit stopping distance should idle immediately.");
                    target.transform.position += Vector3.forward * 20f;
                    yield return null;
                    yield return null;
                    Assert.That(fixture.animator.GetFloat("Speed"), Is.GreaterThan(0f));
                }
                finally
                {
                    Object.DestroyImmediate(floor);
                    if (wall != null) Object.DestroyImmediate(wall);
                    Object.DestroyImmediate(target);
                }
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator VerySlowInPlaceWalkerDoesNotSlideInIdleOrFalselyStall()
        {
            yield return new EnterPlayMode();
            using (var fixture = new WalkerFixture())
            {
                fixture.AddAnimator();
                fixture.walker.animationPlaybackSpeed = 0.05f;
                fixture.walker.progressCheckInterval = 0.25f;
                fixture.walker.stalledChecksBeforeRecovery = 1;
                fixture.root.SetActive(true);
                // This is an Edit Mode test entering Play Mode. Use the game clock
                // explicitly; the editor test runner does not wait on WaitForSeconds.
                float deadline = Time.time + 1.5f;
                while (Time.time < deadline) yield return null;
                Assert.That(fixture.root.transform.position.z, Is.GreaterThan(0.05f),
                    $"blocked={fixture.walker.IsMovementBlocked}, Speed={fixture.animator.GetFloat("Speed")}, " +
                    $"requested={fixture.Get<float>("requestedProgressDistance")}, actual={fixture.Get<float>("actualProgressDistance")}, " +
                    $"activity={fixture.Get<object>("activity")}, enabled={fixture.walker.enabled}");
                Assert.That(fixture.root.transform.position.z, Is.LessThan(0.2f));
                Assert.That(fixture.walker.IsMovementBlocked, Is.False);
                Assert.That(fixture.animator.GetFloat("Speed"), Is.GreaterThan(0f));
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator RootMotionWalkerStartsAndRestartsWithoutInPlaceFallback()
        {
            yield return new EnterPlayMode();
            using (var fixture = new WalkerFixture())
            {
                fixture.AddAnimator(true);
                fixture.walker.locomotionSource = RandomCharacterWalker.LocomotionSource.RootMotion;
                fixture.walker.inPlaceMovementSpeed = 0f;
                fixture.root.SetActive(true);
                float deadline = Time.time + 0.5f;
                while (Time.time < deadline) yield return null;
                Assert.That(fixture.root.transform.position.z, Is.GreaterThan(0.1f),
                    $"Only authored root motion may move this fixture. delta={fixture.animator.deltaPosition}, " +
                    $"Speed={fixture.animator.GetFloat("Speed")}, state={fixture.animator.GetCurrentAnimatorStateInfo(0).shortNameHash}");
                fixture.walker.SetShooterRagdolled(true);
                deadline = Time.time + 0.1f;
                while (Time.time < deadline) yield return null;
                fixture.root.transform.position += Vector3.right * 10f;
                fixture.walker.SetShooterRagdolled(false);
                float before = fixture.root.transform.position.z;
                deadline = Time.time + 0.5f;
                while (Time.time < deadline) yield return null;
                Assert.That(fixture.root.transform.position.z, Is.GreaterThan(before + 0.1f));
                Assert.That(fixture.root.transform.position.x, Is.EqualTo(10f).Within(0.01f));
                Assert.That(fixture.walker.IsMovementBlocked, Is.False);
            }
            yield return new ExitPlayMode();
        }
    }
}
