#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.Dynamics;
using UMA.Dynamics.Examples;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace UMA.Tests
{
    public sealed class UMAPhysicsAvatarRestTests
    {
        private GameObject avatarObject;
        private GameObject ground;
        private UMAPhysicsAvatar avatar;
        private Animator animator;
        private List<Rigidbody> bodies;
        private int started, ended;
        private static readonly MethodInfo FixedUpdate = typeof(UMAPhysicsAvatar).GetMethod(
            "FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        private int RestSteps => Mathf.CeilToInt(avatar.ragdollRestTime / Time.fixedDeltaTime) + 2;

        [SetUp]
        public void SetUp()
        {
            Assert.That(Application.isPlaying, Is.True, "Ragdoll lifecycle tests require Play Mode.");
            avatarObject = new GameObject("Ragdoll rest fixture");
            animator = avatarObject.AddComponent<Animator>();
            avatar = avatarObject.AddComponent<UMAPhysicsAvatar>();
            // Drive FixedUpdate explicitly for deterministic movement-window tests.
            avatar.enabled = false;
            avatar.simplePlayerCollider = false;
            avatar.UpdateTransformAfterRagdoll = false;
            avatar.ragdollRestTime = 0.2f;
            avatar.onRagdollStarted = new UnityEvent();
            avatar.onRagdollEnded = new UnityEvent();
            started = ended = 0;
            avatar.onRagdollStarted.AddListener(() => started++);
            avatar.onRagdollEnded.AddListener(() => ended++);
            bodies = (List<Rigidbody>)typeof(UMAPhysicsAvatar).GetField(
                "_rigidbodies", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(avatar);
            AddBody("Hips", new Vector3(0f, 2f, 0f));
            AddBody("LeftHand", new Vector3(-0.7f, 2f, 0f));
            AddBody("RightFoot", new Vector3(0.7f, 2f, 0f));
            typeof(UMAPhysicsAvatar).GetField("_rootBone", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(avatar, bodies[0].gameObject);
            avatar.ragdolled = true;
        }

        private Rigidbody AddBody(string name, Vector3 position)
        {
            var bone = new GameObject(name);
            bone.transform.SetParent(avatarObject.transform, false);
            bone.transform.position = position;
            bone.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;
            var body = bone.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            bodies.Add(body);
            return body;
        }

        private void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
                FixedUpdate.Invoke(avatar, null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatarObject);
            if (ground != null) Object.DestroyImmediate(ground);
        }

        [Test]
        public void SmallVibrationsFreezeEveryBodyWithoutRecoveringAnimation()
        {
            Vector3 original = bodies[1].position;
            Tick();
            for (int i = 0; i < RestSteps; i++)
            {
                if (!avatar.IsRagdollFrozen)
                {
                    bodies[1].position = original + Vector3.right * (i % 2 == 0 ? 0.005f : -0.005f);
                    bodies[1].rotation = Quaternion.Euler(0f, i % 2 == 0 ? 1f : -1f, 0f);
                    bodies[1].linearVelocity = Vector3.right * 0.01f;
                    bodies[1].angularVelocity = Vector3.up * 0.01f;
                }
                Tick();
            }

            Assert.That(avatar.IsRagdollFrozen, Is.True);
            Assert.That(avatar.ragdolled, Is.True);
            Assert.That(animator.enabled, Is.False);
            Assert.That(started, Is.EqualTo(1));
            Assert.That(ended, Is.Zero);
            foreach (Rigidbody body in bodies)
            {
                Assert.That(body.isKinematic, Is.True);
                Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
                Assert.That(body.angularVelocity, Is.EqualTo(Vector3.zero));
                Assert.That(body.GetComponent<Collider>().enabled, Is.True);
            }
            // The experimental blend must not continue moving a frozen pose.
            Quaternion frozenRotation = bodies[1].rotation;
            avatar.ragdollBlendAmount = 1f;
            Tick(RestSteps);
            Assert.That(bodies[1].rotation, Is.EqualTo(frozenRotation));
        }

        [Test]
        public void MovingExtremityPreventsStationaryHipsFromFreezing()
        {
            Tick();
            for (int i = 0; i < RestSteps * 4; i++)
            {
                bodies[1].position += Vector3.right * 0.01f;
                Tick();
                Assert.That(avatar.IsRagdollFrozen, Is.False);
            }
            Tick(RestSteps);
            Assert.That(avatar.IsRagdollFrozen, Is.True);
        }

        [Test]
        public void RotatingLimbPreventsRestEvenWithoutTranslation()
        {
            Tick();
            for (int i = 0; i < RestSteps * 4; i++)
            {
                bodies[2].rotation = Quaternion.Euler(0f, i * 2f, 0f);
                Tick();
                Assert.That(avatar.IsRagdollFrozen, Is.False);
            }
        }

        [Test]
        public void SlowWholeBodyDriftDoesNotAccumulateRestTime()
        {
            Tick();
            for (int i = 0; i < RestSteps * 4; i++)
            {
                foreach (Rigidbody body in bodies)
                    body.position += Vector3.down * 0.01f;
                Tick();
                Assert.That(avatar.IsRagdollFrozen, Is.False);
            }
        }

        [Test]
        public void WakeRestartsRestWindowAndRecoveryStillWorks()
        {
            Tick(RestSteps);
            Assert.That(avatar.IsRagdollFrozen, Is.True);
            avatar.WakeRagdoll();
            Assert.That(avatar.IsRagdollFrozen, Is.False);
            Assert.That(avatar.ragdolled, Is.True);
            Assert.That(animator.enabled, Is.False);
            Assert.That(started, Is.EqualTo(1));
            Assert.That(ended, Is.Zero);
            foreach (Rigidbody body in bodies) Assert.That(body.isKinematic, Is.False);
            Tick();
            Assert.That(avatar.IsRagdollFrozen, Is.False, "Waking must start a new full rest window.");
            Tick(RestSteps);
            Assert.That(avatar.IsRagdollFrozen, Is.True);
            avatar.UpdateTransformAfterRagdoll = true;
            Vector3 hipPosition = bodies[0].position;
            avatar.ragdolled = false;
            Assert.That(avatarObject.transform.position, Is.EqualTo(hipPosition));
            Assert.That(avatar.IsRagdollFrozen, Is.False);
            Assert.That(animator.enabled, Is.True);
            Assert.That(ended, Is.EqualTo(1));
            avatar.WakeRagdoll();
            foreach (Rigidbody body in bodies) Assert.That(body.isKinematic, Is.True);
            avatar.ragdolled = true;
            foreach (Rigidbody body in bodies) Assert.That(body.isKinematic, Is.False);
        }

        [Test]
        public void DisablingAutomaticRestResumesFrozenPhysics()
        {
            avatar.freezeWhenRagdollRests = false;
            Tick(RestSteps * 3);
            Assert.That(avatar.IsRagdollFrozen, Is.False);
            avatar.freezeWhenRagdollRests = true;
            Tick(RestSteps);
            Assert.That(avatar.IsRagdollFrozen, Is.True);
            avatar.freezeWhenRagdollRests = false;
            Tick();
            Assert.That(avatar.IsRagdollFrozen, Is.False);
            foreach (Rigidbody body in bodies) Assert.That(body.isKinematic, Is.False);
        }

        [Test]
        public void EmptyOrDestroyedBodyListsDoNotFreezeOrThrow()
        {
            foreach (Rigidbody body in bodies) Object.DestroyImmediate(body.gameObject);
            Tick(RestSteps);
            Assert.That(avatar.IsRagdollFrozen, Is.False);
            bodies.Clear();
            Tick(RestSteps);
            Assert.That(avatar.IsRagdollFrozen, Is.False);
        }

        [Test]
        public void RepeatedWakeCallsKeepAnActiveRagdollFromFreezing()
        {
            for (int i = 0; i < 4; i++)
            {
                Tick(RestSteps - 3);
                avatar.WakeRagdoll();
                Assert.That(avatar.IsRagdollFrozen, Is.False);
            }
            Tick(RestSteps);
            Assert.That(avatar.IsRagdollFrozen, Is.True);
        }

        [Test]
        public void PhysicsSlotCopiesRestSettingsToItsGeneratedAvatar()
        {
            var slotObject = new GameObject("Physics slot settings fixture");
            var generatedObject = new GameObject("Generated physics settings fixture");
            try
            {
                var slot = slotObject.AddComponent<UMAPhysicsSlotDefinition>();
                slot.simplePlayerCollider = false;
                slot.PhysicsElements = new List<UMAPhysicsElement>();
                slot.freezeWhenRagdollRests = false;
                slot.ragdollRestTime = 2.5f;
                slot.ragdollRestPositionThreshold = 0.06f;
                slot.ragdollRestRotationThreshold = 12f;
                var data = generatedObject.AddComponent<UMAData>();
                data.CharacterCreated = new UMADataEvent();
                data.CharacterBegun = new UMADataEvent();
                data.CharacterUpdated = new UMADataEvent();
                data.SetRenderers(new SkinnedMeshRenderer[0]);
                slot.OnSkeletonAvailable(data);
                var generated = generatedObject.GetComponent<UMAPhysicsAvatar>();
                Assert.That(generated.freezeWhenRagdollRests, Is.False);
                Assert.That(generated.ragdollRestTime, Is.EqualTo(2.5f));
                Assert.That(generated.ragdollRestPositionThreshold, Is.EqualTo(0.06f));
                Assert.That(generated.ragdollRestRotationThreshold, Is.EqualTo(12f));
            }
            finally
            {
                Object.DestroyImmediate(generatedObject);
                Object.DestroyImmediate(slotObject);
            }
        }

        [UnityTest]
        public IEnumerator FallingJointedBodiesSettleThenRespondToShooterImpact()
        {
            // Keep the fixture away from any scene content the project starts with.
            Vector3 offset = new Vector3(1000f, 0f, 1000f);
            foreach (Rigidbody body in bodies)
            {
                body.position += offset;
                body.useGravity = true;
            }
            for (int i = 1; i < bodies.Count; i++)
                bodies[i].gameObject.AddComponent<CharacterJoint>().connectedBody = bodies[0];
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ragdoll rest ground";
            ground.transform.position = offset + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            Physics.SyncTransforms();

            for (int i = 0; i < 500 && !avatar.IsRagdollFrozen; i++)
            {
                yield return new WaitForFixedUpdate();
                Tick();
            }
            Assert.That(avatar.IsRagdollFrozen, Is.True, "Jointed bodies should freeze after landing.");
            Assert.That(bodies[0].position.y, Is.LessThan(0.5f), "Do not freeze during the fall.");
            Vector3 settledPosition = bodies[0].position;
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Assert.That(bodies[0].position, Is.EqualTo(settledPosition));

            var shooterObject = new GameObject("Ragdoll wake shooter fixture");
            try
            {
                var shooter = shooterObject.AddComponent<UMAShooter>();
                shooter.enabled = false;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(UMAShooter).GetField("impactTarget", flags).SetValue(shooter, bodies[0]);
                typeof(UMAShooter).GetField("impactAvatar", flags).SetValue(shooter, avatar);
                typeof(UMAShooter).GetField("impactEndTime", flags).SetValue(shooter, Time.time + 0.1f);
                // The impulse is shared by three connected bodies; use enough to lift them.
                typeof(UMAShooter).GetField("impact", flags).SetValue(shooter, Vector3.up * 6f);
                typeof(UMAShooter).GetMethod("Update", flags).Invoke(shooter, null);
                Assert.That(avatar.IsRagdollFrozen, Is.False);
                foreach (Rigidbody body in bodies) Assert.That(body.isKinematic, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(shooterObject);
            }
            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
            Assert.That(avatar.IsRagdollFrozen, Is.False);
            Assert.That(Vector3.Distance(bodies[0].position, settledPosition), Is.GreaterThan(0.01f));
        }
    }
}
#endif
