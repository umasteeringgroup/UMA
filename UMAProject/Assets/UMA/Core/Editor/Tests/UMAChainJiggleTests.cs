#if UNITY_EDITOR

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Behaviour tests for <see cref="UMAChainJiggle"/>. Every test drives the solver with
    /// <see cref="UMAChainJiggle.Simulate"/> on a small hand-built bone chain, so no scene,
    /// Play mode or generated character is needed.
    /// </summary>
    public sealed class UMAChainJiggleTests
    {
        private const float BoneLength = 0.06f;
        private const int BoneCount = 6;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        private sealed class Chain
        {
            public Transform holder;
            public Transform[] bones;
            public UMAChainJiggle jiggle;

            public Transform Tip => bones[bones.Length - 1];

            public void Run(float seconds, float frameRate = 60f)
            {
                int frames = Mathf.RoundToInt(seconds * frameRate);
                for (int i = 0; i < frames; i++)
                {
                    jiggle.Simulate(1f / frameRate);
                }
            }
        }

        private Chain MakeChain(Vector3 boneStep, UMAChainJiggleSettings settings = null, int boneCount = BoneCount)
        {
            GameObject holder = new GameObject("Chain holder") { hideFlags = HideFlags.HideAndDontSave };
            _created.Add(holder);
            holder.transform.position = new Vector3(3f, 1.6f, -2f);

            Chain chain = new Chain { holder = holder.transform, bones = new Transform[boneCount] };
            Transform parent = holder.transform;
            for (int i = 0; i < boneCount; i++)
            {
                GameObject bone = new GameObject("Bone" + i) { hideFlags = HideFlags.HideAndDontSave };
                bone.transform.SetParent(parent, false);
                bone.transform.localPosition = i == 0 ? Vector3.zero : boneStep;
                chain.bones[i] = bone.transform;
                parent = bone.transform;
            }

            chain.jiggle = chain.bones[0].gameObject.AddComponent<UMAChainJiggle>();
            chain.jiggle.autoSimulate = false;
            chain.jiggle.Setup(chain.bones[0], settings ?? new UMAChainJiggleSettings());
            // The first step only snaps the chain to the authored pose.
            chain.jiggle.Simulate(1f / 60f);
            return chain;
        }

        private static Chain Hanging(UMAChainJiggleTests fixture, UMAChainJiggleSettings settings = null)
        {
            return fixture.MakeChain(new Vector3(0f, -BoneLength, 0f), settings);
        }

        private static float MaxBoneLengthError(Chain chain, float expected = BoneLength)
        {
            float worst = 0f;
            for (int i = 1; i < chain.bones.Length; i++)
            {
                worst = Mathf.Max(worst, Mathf.Abs(Vector3.Distance(chain.bones[i].position, chain.bones[i - 1].position) - expected));
            }

            return worst;
        }

        /// <summary>Moves the holder 0.5 m in 0.25 s, then holds still. Returns the largest tip lag seen.</summary>
        private static float StepResponse(Chain chain, float frameRate, float seconds, System.Func<int, float> frameTime = null)
        {
            Vector3 start = chain.holder.position;
            Vector3 tipRest = chain.Tip.position - chain.bones[0].position;
            float peak = 0f;
            float time = 0f;
            int frame = 0;
            while (time < seconds)
            {
                float dt = frameTime != null ? frameTime(frame) : 1f / frameRate;
                time += dt;
                frame++;
                chain.holder.position = start + new Vector3(0.5f * Mathf.Clamp01(time / 0.25f), 0f, 0f);
                chain.jiggle.Simulate(dt);
                peak = Mathf.Max(peak, Vector3.Distance(chain.Tip.position, chain.bones[0].position + tipRest));
            }

            return peak;
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void AuthoredShapeHoldsStillWhenNothingMoves()
        {
            // A chain modelled at an angle (a pigtail sticking out) must not sag or drift.
            Chain chain = MakeChain(new Vector3(0.05f, -0.03f, 0f));
            Vector3 tip = chain.Tip.position;

            chain.Run(5f);

            Assert.That(Vector3.Distance(chain.Tip.position, tip), Is.LessThan(1e-5f));
            for (int i = 0; i < chain.bones.Length; i++)
            {
                Assert.That(Quaternion.Angle(chain.bones[i].localRotation, Quaternion.identity), Is.LessThan(0.001f), "Bone " + i);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void ChainLagsBehindAMoveThenSettlesBackOnTheAuthoredPose()
        {
            Chain chain = Hanging(this);
            Vector3 tipRest = chain.Tip.position - chain.bones[0].position;

            float peak = StepResponse(chain, 60f, 0.5f);
            Assert.That(peak, Is.GreaterThan(0.03f), "The chain should visibly lag behind a sudden move.");

            chain.Run(4f);
            Assert.That(Vector3.Distance(chain.Tip.position, chain.bones[0].position + tipRest), Is.LessThan(0.001f), "The chain should return to its authored pose.");
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void BonesNeverStretch()
        {
            Chain chain = Hanging(this);
            Vector3 start = chain.holder.position;
            float worst = 0f;
            for (int i = 0; i < 240; i++)
            {
                float t = i / 60f;
                chain.holder.position = start + new Vector3(Mathf.Sin(t * 17f) * 0.4f, Mathf.Sin(t * 11f) * 0.3f, Mathf.Cos(t * 13f) * 0.4f);
                chain.holder.rotation = Quaternion.Euler(Mathf.Sin(t * 7f) * 80f, t * 400f, Mathf.Cos(t * 5f) * 60f);
                chain.jiggle.Simulate(1f / 60f);
                worst = Mathf.Max(worst, MaxBoneLengthError(chain));
            }

            Assert.That(worst, Is.LessThan(1e-4f));
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void MotionIsTheSameAtAnyFrameRate()
        {
            float reference = StepResponse(Hanging(this), 60f, 2f);
            foreach (float frameRate in new[] { 20f, 30f, 45f, 90f, 144f, 240f })
            {
                float peak = StepResponse(Hanging(this), frameRate, 2f);
                Assert.That(peak, Is.EqualTo(reference).Within(reference * 0.05f), frameRate + " fps");
            }

            // Uneven frame times, as a real game produces.
            System.Random random = new System.Random(7);
            float uneven = StepResponse(Hanging(this), 0f, 2f, frame => 0.006f + (float)random.NextDouble() * 0.02f);
            Assert.That(uneven, Is.EqualTo(reference).Within(reference * 0.05f), "uneven frame times");
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void SettingUpAgainWhileSwingingKeepsTheAuthoredPose()
        {
            // UMA calls Setup on every character rebuild, often in the middle of a swing.
            Chain chain = Hanging(this);
            StepResponse(chain, 60f, 0.2f);
            Assert.That(Quaternion.Angle(chain.bones[2].localRotation, Quaternion.identity), Is.GreaterThan(1f), "Test needs the chain to be swinging.");

            chain.jiggle.Setup(chain.bones[0], new UMAChainJiggleSettings());

            for (int i = 0; i < chain.bones.Length; i++)
            {
                Assert.That(Quaternion.Angle(chain.bones[i].localRotation, Quaternion.identity), Is.LessThan(0.001f), "Bone " + i + " right after Setup");
            }

            chain.Run(4f);
            for (int i = 0; i < chain.bones.Length; i++)
            {
                Assert.That(Quaternion.Angle(chain.bones[i].localRotation, Quaternion.identity), Is.LessThan(0.01f), "Bone " + i + " after settling");
            }
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void PoseWrittenByAnythingElseBecomesTheNewAuthoredPose()
        {
            // UMA resets bones and applies DNA on rebuilds, and an Animator may drive the chain.
            Chain chain = Hanging(this);
            StepResponse(chain, 60f, 0.2f);

            Quaternion written = Quaternion.Euler(0f, 0f, 25f);
            Vector3 moved = new Vector3(0.01f, -0.08f, 0f);
            chain.bones[2].localRotation = written;
            chain.bones[3].localPosition = moved;

            chain.Run(4f);

            Assert.That(Quaternion.Angle(chain.bones[2].localRotation, written), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(chain.bones[3].localPosition, moved), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(chain.bones[3].position, chain.bones[2].position), Is.EqualTo(moved.magnitude).Within(1e-4f), "The new bone length is respected.");
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void TiltingTheParentLetsGravityPullTheChainDown()
        {
            Chain chain = Hanging(this);
            chain.holder.rotation = Quaternion.Euler(0f, 0f, 90f);
            chain.Run(4f);

            Vector3 rigid = chain.holder.rotation * Vector3.down;
            Vector3 direction = (chain.Tip.position - chain.bones[0].position).normalized;
            Assert.That(Vector3.Angle(direction, Vector3.down), Is.LessThan(Vector3.Angle(rigid, Vector3.down) - 15f), "The chain should hang closer to straight down than a rigid chain.");

            Chain weightless = Hanging(this, new UMAChainJiggleSettings { gravity = 0f });
            weightless.holder.rotation = Quaternion.Euler(0f, 0f, 90f);
            weightless.Run(4f);
            Vector3 weightlessDirection = (weightless.Tip.position - weightless.bones[0].position).normalized;
            Assert.That(Vector3.Angle(weightlessDirection, rigid), Is.LessThan(0.5f), "Without gravity the chain follows its parent.");
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void StifferSettingsSwingLess()
        {
            float loose = StepResponse(Hanging(this, new UMAChainJiggleSettings { stiffness = 0f, elasticity = 0.05f }), 60f, 1f);
            float springy = StepResponse(Hanging(this, new UMAChainJiggleSettings { stiffness = 0f, elasticity = 0.4f }), 60f, 1f);
            float stiff = StepResponse(Hanging(this, new UMAChainJiggleSettings { stiffness = 0.9f, elasticity = 0.05f }), 60f, 1f);
            float rigid = StepResponse(Hanging(this, new UMAChainJiggleSettings { stiffness = 1f }), 60f, 1f);
            float carried = StepResponse(Hanging(this, new UMAChainJiggleSettings { stiffness = 0f, elasticity = 0.05f, inertia = 1f }), 60f, 1f);

            Assert.That(springy, Is.LessThan(loose));
            Assert.That(stiff, Is.LessThan(loose * 0.5f));
            Assert.That(rigid, Is.LessThan(1e-4f));
            Assert.That(carried, Is.LessThan(1e-4f), "Inertia 1 ignores the character's own travel.");
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void BoneWithNoLengthStaysOnItsParentWhichSwingsForBoth()
        {
            // The UMA breast rig: LeftOuterBreast > LeftInnerBreast (same position) > LeftInnerBreast_end.
            Chain chain = MakeChain(Vector3.zero, new UMAChainJiggleSettings { endLength = 0f }, 3);
            chain.bones[2].localPosition = new Vector3(0f, 0.095f, 0f);
            chain.jiggle.Setup(chain.bones[0], new UMAChainJiggleSettings { endLength = 0f });
            chain.jiggle.Simulate(1f / 60f);

            Vector3 start = chain.holder.position;
            float largest = 0f;
            for (int i = 0; i < 120; i++)
            {
                chain.holder.position = start + new Vector3(Mathf.Sin(i / 60f * Mathf.PI * 5f) * 0.03f, 0f, 0f);
                chain.jiggle.Simulate(1f / 60f);
                largest = Mathf.Max(largest, Quaternion.Angle(chain.bones[0].localRotation, Quaternion.identity));
                Assert.That(Vector3.Distance(chain.bones[1].position, chain.bones[0].position), Is.LessThan(1e-6f));
                Assert.That(Vector3.Distance(chain.bones[2].position, chain.bones[1].position), Is.EqualTo(0.095f).Within(1e-4f));
            }

            // Skin is weighted to both bones, so the outer one turns and carries the inner one with it.
            Assert.That(largest, Is.GreaterThan(1f), "The outer bone should bounce.");
            Assert.That(largest, Is.LessThan(60f), "It should not flail.");
            Assert.That(Quaternion.Angle(chain.bones[1].localRotation, Quaternion.identity), Is.LessThan(0.001f), "The zero-length bone keeps its own pose.");

            chain.Run(4f);
            Assert.That(Quaternion.Angle(chain.bones[0].localRotation, Quaternion.identity), Is.LessThan(0.01f), "It settles back on the authored pose.");
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void LeafBoneGetsAVirtualTipSoItCanRotate()
        {
            Chain chain = MakeChain(new Vector3(0f, -BoneLength, 0f), new UMAChainJiggleSettings { endLength = 0.1f }, 1);
            Assert.That(chain.jiggle.BoneCount, Is.EqualTo(1));
            Assert.That(chain.jiggle.ParticleCount, Is.EqualTo(2));

            float peak = 0f;
            Vector3 start = chain.holder.position;
            for (int i = 0; i < 60; i++)
            {
                chain.holder.position = start + new Vector3(Mathf.Sin(i / 60f * Mathf.PI * 6f) * 0.05f, 0f, 0f);
                chain.jiggle.Simulate(1f / 60f);
                peak = Mathf.Max(peak, Quaternion.Angle(chain.bones[0].localRotation, Quaternion.identity));
            }

            Assert.That(peak, Is.GreaterThan(1f));
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void CollidersKeepJointsOutside()
        {
            Chain chain = Hanging(this, new UMAChainJiggleSettings { radius = 0.01f, stiffness = 0f, elasticity = 0.02f });
            GameObject obstacle = new GameObject("Obstacle") { hideFlags = HideFlags.HideAndDontSave };
            _created.Add(obstacle);
            // A shoulder-sized sphere beside the chain, in the way of a swing to +X.
            obstacle.transform.position = chain.bones[0].position + new Vector3(0.12f, -0.2f, 0f);
            UMAChainJiggleCollider collider = new UMAChainJiggleCollider { bone = obstacle.transform, radius = 0.08f };
            chain.jiggle.Setup(chain.bones[0], chain.jiggle.settings, null, new[] { collider });
            chain.jiggle.Simulate(1f / 60f);

            // Shove the holder away from the sphere so the chain swings into it, then let it hang.
            Vector3 start = chain.holder.position;
            bool touched = false;
            for (int i = 0; i < 180; i++)
            {
                float t = i / 60f;
                Vector3 offset = new Vector3(-0.5f * Mathf.Clamp01(t / 0.2f) + 0.5f * Mathf.Clamp01((t - 0.3f) / 0.2f), 0f, 0f);
                chain.holder.position = start + offset;
                obstacle.transform.position = chain.bones[0].position + new Vector3(0.12f, -0.2f, 0f);
                chain.jiggle.Simulate(1f / 60f);
                for (int p = 1; p < chain.jiggle.ParticleCount; p++)
                {
                    float distance = Vector3.Distance(chain.jiggle.GetParticlePosition(p), obstacle.transform.position);
                    // Bone length wins over the collider, so a hard slam may dip a few millimetres
                    // in its worst frame (measured 4.5 mm here); without colliders it passes straight through.
                    Assert.That(distance, Is.GreaterThan(0.08f + 0.01f - 0.006f), "Joint " + p + " at frame " + i);
                    touched |= distance < 0.08f + 0.01f + 0.002f;
                }
            }

            Assert.That(touched, Is.True, "Test needs the chain to reach the collider.");
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void TeleportCarriesTheChainWithoutWhipping()
        {
            Chain chain = Hanging(this);
            Vector3 before = chain.Tip.position - chain.bones[0].position;

            chain.holder.position += new Vector3(40f, 2f, -15f);
            chain.jiggle.Simulate(1f / 60f);
            Assert.That(Vector3.Distance(chain.Tip.position - chain.bones[0].position, before), Is.LessThan(1e-4f));

            float worst = 0f;
            for (int i = 0; i < 60; i++)
            {
                chain.jiggle.Simulate(1f / 60f);
                worst = Mathf.Max(worst, Vector3.Distance(chain.Tip.position - chain.bones[0].position, before));
            }

            Assert.That(worst, Is.LessThan(1e-3f));
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void SurvivesPausedHugeAndBrokenFrames()
        {
            Chain chain = Hanging(this);
            StepResponse(chain, 60f, 0.2f);

            chain.jiggle.Simulate(0f);
            chain.jiggle.Simulate(5f);
            chain.jiggle.Simulate(float.Epsilon);
            chain.holder.position += Vector3.right * 0.3f;
            chain.jiggle.Simulate(0.5f);

            Assert.That(MaxBoneLengthError(chain), Is.LessThan(1e-4f));
            for (int i = 0; i < chain.bones.Length; i++)
            {
                Vector3 position = chain.bones[i].position;
                Assert.That(float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z), Is.False);
            }

            // A bone removed from under the chain (wardrobe change) rebuilds it instead of throwing.
            Object.DestroyImmediate(chain.bones[4].gameObject);
            Assert.DoesNotThrow(() => chain.jiggle.Simulate(1f / 60f));
            Assert.That(chain.jiggle.BoneCount, Is.EqualTo(4));
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void BlendWeightFadesBetweenAuthoredAndSimulatedPose()
        {
            UMAChainJiggleSettings settings = new UMAChainJiggleSettings();
            Chain chain = Hanging(this, settings);
            StepResponse(chain, 60f, 0.2f);
            float full = Quaternion.Angle(chain.bones[1].localRotation, Quaternion.identity);
            Assert.That(full, Is.GreaterThan(1f));

            settings.blendWeight = 0f;
            chain.jiggle.Simulate(0f);
            for (int i = 0; i < chain.bones.Length; i++)
            {
                Assert.That(Quaternion.Angle(chain.bones[i].localRotation, Quaternion.identity), Is.LessThan(0.001f));
            }

            settings.blendWeight = 0.5f;
            chain.jiggle.Simulate(0f);
            float half = Quaternion.Angle(chain.bones[1].localRotation, Quaternion.identity);
            Assert.That(half, Is.GreaterThan(full * 0.2f).And.LessThan(full * 0.8f));
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void OneAnimatorAssetDrivesSeveralCharactersIndependently()
        {
            UMAChainJiggleAnimator animator = ScriptableObject.CreateInstance<UMAChainJiggleAnimator>();
            _created.Add(animator);
            animator.Chains.Add(new UMAChainJiggleAnimator.ChainDefinition { AnchorBoneName = "Tail" });
            animator.Colliders.Add(new UMAChainJiggleAnimator.ColliderDefinition { BoneName = "Head", Radius = 0.05f });

            UMAData first = MakeCharacter(out Transform firstTail);
            UMAData second = MakeCharacter(out Transform secondTail);

            animator.Initialize(first, null);
            animator.Initialize(second, null);
            animator.Initialize(first, null);

            UMAChainJiggle firstChain = firstTail.GetComponent<UMAChainJiggle>();
            UMAChainJiggle secondChain = secondTail.GetComponent<UMAChainJiggle>();
            Assert.That(firstChain, Is.Not.Null);
            Assert.That(secondChain, Is.Not.Null);
            Assert.That(firstTail.GetComponents<UMAChainJiggle>().Length, Is.EqualTo(1), "Setting up again reuses the component.");
            Assert.That(firstChain.BoneCount, Is.EqualTo(3));
            Assert.That(firstChain.colliders.Count, Is.EqualTo(1));
            Assert.That(firstChain.colliders[0].bone, Is.SameAs(firstTail.parent));
            Assert.That(secondChain.colliders[0].bone, Is.SameAs(secondTail.parent));
            Assert.That(firstChain.settings, Is.SameAs(animator.settings), "Edits to the asset reach every character.");

            // Only the first character moves; only its chain swings.
            firstChain.autoSimulate = false;
            secondChain.autoSimulate = false;
            firstChain.Simulate(1f / 60f);
            secondChain.Simulate(1f / 60f);
            Vector3 start = first.transform.position;
            for (int i = 0; i < 12; i++)
            {
                first.transform.position = start + new Vector3(0.04f * i, 0f, 0f);
                firstChain.Simulate(1f / 60f);
                secondChain.Simulate(1f / 60f);
            }

            Assert.That(Quaternion.Angle(firstTail.localRotation, Quaternion.identity), Is.GreaterThan(1f));
            Assert.That(Quaternion.Angle(secondTail.localRotation, Quaternion.identity), Is.LessThan(0.001f));
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void ChainRemovesItselfWhenItsSlotLeavesTheCharacter()
        {
            UMAChainJiggleAnimator animator = ScriptableObject.CreateInstance<UMAChainJiggleAnimator>();
            _created.Add(animator);
            animator.Chains.Add(new UMAChainJiggleAnimator.ChainDefinition { AnchorBoneName = "Tail" });

            UMAData character = MakeCharacter(out Transform tail);
            animator.Initialize(character, null);
            UMAChainJiggle chain = tail.GetComponent<UMAChainJiggle>();
            MethodInfo lateUpdate = typeof(UMAChainJiggle).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

            // Swing it so there is a simulated pose to put back.
            lateUpdate.Invoke(chain, null);
            chain.autoSimulate = false;
            Vector3 start = character.transform.position;
            for (int i = 0; i < 12; i++)
            {
                character.transform.position = start + new Vector3(0.04f * i, 0f, 0f);
                chain.Simulate(1f / 60f);
            }

            chain.autoSimulate = true;
            Assert.That(Quaternion.Angle(tail.localRotation, Quaternion.identity), Is.GreaterThan(1f));

            // The character is rebuilt and no bone animator asks for this chain any more.
            character.SetupEmbeddedPhysics();
            lateUpdate.Invoke(chain, null);

            Assert.That(chain == null, Is.True, "The stale component should be gone.");
            Assert.That(Quaternion.Angle(tail.localRotation, Quaternion.identity), Is.LessThan(0.001f), "The bones are left in their authored pose.");
        }

        [Test]
        [Category("UMA")]
        [Category("Physics")]
        public void AnimatorLeavesExcludedRacesAlone()
        {
            // Several races can share one base recipe; only some of them should get the chain.
            UMAChainJiggleAnimator animator = ScriptableObject.CreateInstance<UMAChainJiggleAnimator>();
            _created.Add(animator);
            animator.Chains.Add(new UMAChainJiggleAnimator.ChainDefinition { AnchorBoneName = "Tail" });
            animator.ExcludedRaces.Add("Elf");

            RaceData race = ScriptableObject.CreateInstance<RaceData>();
            _created.Add(race);
            race._oldRaceName = "Elf";

            UMAData elf = MakeCharacter(out Transform elfTail);
            elf.umaRecipe.raceData = race;
            animator.Initialize(elf, null);
            Assert.That(elfTail.GetComponent<UMAChainJiggle>(), Is.Null, "An excluded race gets no chain.");

            UMAData noRace = MakeCharacter(out Transform otherTail);
            animator.Initialize(noRace, null);
            Assert.That(otherTail.GetComponent<UMAChainJiggle>(), Is.Not.Null, "Other characters still get one.");

            // A character that had the chain and is rebuilt as an excluded race loses it.
            race._oldRaceName = "Human";
            animator.Initialize(elf, null);
            UMAChainJiggle chain = elfTail.GetComponent<UMAChainJiggle>();
            Assert.That(chain, Is.Not.Null);

            race._oldRaceName = "Elf";
            elf.SetupEmbeddedPhysics();
            animator.Initialize(elf, null);
            typeof(UMAChainJiggle).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(chain, null);
            Assert.That(chain == null, Is.True, "The chain from the earlier build removes itself.");
        }

        private UMAData MakeCharacter(out Transform tail)
        {
            GameObject avatar = new GameObject("Jiggle test avatar") { hideFlags = HideFlags.HideAndDontSave };
            _created.Add(avatar);
            Transform root = Child(avatar.transform, "Root", Vector3.zero);
            Transform head = Child(root, "Head", new Vector3(0f, 1.6f, 0f));
            tail = Child(head, "Tail", new Vector3(0f, 0f, -0.1f));
            Transform tail1 = Child(tail, "Tail1", new Vector3(0f, -BoneLength, 0f));
            Child(tail1, "Tail2", new Vector3(0f, -BoneLength, 0f));

            UMAData data = avatar.AddComponent<UMAData>();
            data.umaRecipe = new UMAData.UMARecipe();
            data.skeleton = new UMAImprovedSkeleton(root);
            return data;
        }

        private static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            GameObject child = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            return child.transform;
        }
    }
}

#endif
