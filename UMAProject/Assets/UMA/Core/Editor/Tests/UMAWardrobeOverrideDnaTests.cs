using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMAWardrobeOverrideDnaTests
    {
        private const string Shoulder = "Outerwear_Shoulders";
        private readonly List<Object> objects = new List<Object>();
        private DynamicCharacterAvatar avatar;
        private UMAPredefinedDNA overrides;
        private DNAInstance shoulder;

        private T Keep<T>(T value) where T : Object { objects.Add(value); return value; }

        [SetUp]
        public void SetUp()
        {
            var race = Keep(ScriptableObject.CreateInstance<RaceData>());
            race.name = "Wardrobe override test race";
            race.useNewDNA = true;
            var group = Keep(ScriptableObject.CreateInstance<DNAGroup>());
            group.name = "Wardrobe";
            var definition = Keep(ScriptableObject.CreateInstance<DNA>());
            definition.name = Shoulder;
            group.dnaList.Add(definition);
            race.DNACollection = new DNACollection { DNAGroups = new List<DNAGroup> { group } };
            var root = Keep(new GameObject("Wardrobe override test avatar"));
            root.SetActive(false);
            avatar = root.AddComponent<DynamicCharacterAvatar>();
            avatar.activeRace.name = race.raceName;
            avatar.activeRace.data = race;
            avatar.InitializeAvatar();
            avatar.InitializeAvatar(); // Reinitialization must not stack subscriptions.
            shoulder = new DNAInstance(Shoulder, 0.5f, group);
            avatar.umaRecipe = new UMAData.UMARecipe
            {
                raceData = race,
                slotDataList = Array.Empty<SlotData>(),
                dnaInstanceCollection = new DNAInstanceCollection()
            };
            avatar.umaRecipe.dnaInstanceCollection.dnaInstances.Add(shoulder);
            avatar.umaRecipe.dnaInstanceCollection.Initialize(race.DNACollection);
            Assert.That(avatar.GetDNA().ContainsKey(Shoulder), Is.True);
            overrides = (UMAPredefinedDNA)typeof(DynamicCharacterAvatar)
                .GetField("overrideDNA", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(avatar);
            overrides.AddDNA(Shoulder, 1f);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [TestCase(0.5f)]
        [TestCase(0.7f)]
        public void TextureOnlyUpdateRestoresPersonalDnaBeforeNotifications(float original)
        {
            shoulder.Value = original;
            avatar.isShapeDirty = true;
            avatar.FireCharacterBegunEvents();
            Assert.That(shoulder.Value, Is.EqualTo(1f));
            avatar.FireDNAAppliedEvents();
            Assert.That(shoulder.Value, Is.EqualTo(original));
            avatar.FireUpdatedEvent(false);

            avatar.isShapeDirty = false;
            avatar.isTextureDirty = true;
            avatar.FireCharacterBegunEvents();
            Assert.That(shoulder.Value, Is.EqualTo(1f), "Pre-apply color/material DNA must still see wardrobe overrides.");
            float observed = -1f;
            avatar.OnCharacterUpdated += _ => observed = shoulder.Value;
            avatar.FireUpdatedEvent(false); // No DNA-updated event for a texture-only generation.
            Assert.That(observed, Is.EqualTo(original));
            overrides.Clear(); // BuildCharacter collects no override once the jacket is removed.
            avatar.FireCharacterBegunEvents();
            Assert.That(shoulder.Value, Is.EqualTo(original));
        }

        [Test]
        public void RemovingWardrobeAfterTextureUpdateRestoresBlendshapeWeight()
        {
            var mesh = Keep(new Mesh());
            mesh.vertices = new[] { Vector3.zero };
            mesh.AddBlendShapeFrame(Shoulder, 100f, new[] { Vector3.up }, new Vector3[1], new Vector3[1]);
            var renderer = avatar.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            avatar.SetRenderers(new[] { renderer });
            var effect = new DNAEffect_BlendShape { BlendShapeName = Shoulder, curve = AnimationCurve.Linear(0, 0, 1, 1) };
            avatar.FireCharacterBegunEvents();
            effect.PostApply(avatar, null, shoulder.Value);
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(100f));
            avatar.FireDNAAppliedEvents();
            avatar.FireCharacterBegunEvents();
            avatar.FireUpdatedEvent(false);
            overrides.Clear();
            avatar.FireCharacterBegunEvents();
            effect.PostApply(avatar, null, shoulder.Value);
            Assert.That(renderer.GetBlendShapeWeight(0), Is.Zero.Within(0.001f));
            avatar.FireDNAAppliedEvents();
        }

        [Test]
        public void RestorationIsConsumedAndDoesNotOverwriteLaterDnaEdits()
        {
            avatar.FireCharacterBegunEvents();
            avatar.FireDNAAppliedEvents();
            shoulder.Value = 0.65f;
            avatar.RestoreOverrideDna(avatar);
            avatar.FireUpdatedEvent(false);
            Assert.That(shoulder.Value, Is.EqualTo(0.65f));
        }

        [Test]
        public void RestartedGenerationDoesNotSaveThePreviousOverrideAsPersonalDna()
        {
            avatar.FireCharacterBegunEvents();
            avatar.FireCharacterBegunEvents(); // The previous attempt never reached completion.
            avatar.FireDNAAppliedEvents();
            Assert.That(shoulder.Value, Is.EqualTo(0.5f));
        }

        [Test]
        public void CancelledUpdateRestoresPersonalDna()
        {
            avatar.FireCharacterBegunEvents();
            avatar.FireUpdatedEvent(true);
            Assert.That(shoulder.Value, Is.EqualTo(0.5f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratorCompletionRestoresDnaEvenWhenNotificationsAreDisabled(bool fireEvents)
        {
            var root = Keep(new GameObject("Override completion generator"));
            root.SetActive(false);
            var generator = root.AddComponent<OverrideDnaCompletionTestGenerator>();
            avatar.FireCharacterBegunEvents();
            generator.Complete(avatar, fireEvents);
            Assert.That(shoulder.Value, Is.EqualTo(0.5f));
        }
    }

    public sealed class OverrideDnaCompletionTestGenerator : UMAGenerator
    {
        public override void Awake() { }
        public void Complete(UMAData data, bool fireEvents) { umaData = data; UMAReady(fireEvents); }
    }
}
