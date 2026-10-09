using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UMA.PoseTools;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMADnaOverrideSlotTests
    {
        private readonly List<Object> objects = new List<Object>();
        private T Keep<T>(T obj) where T : Object { objects.Add(obj); return obj; }
        private SlotDataAsset MakeSlot(string name)
        {
            var slot = Keep(ScriptableObject.CreateInstance<SlotDataAsset>());
            slot.name = name;
            return slot;
        }

        [TearDown]
        public void Cleanup()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DcaRecipePreparedRunsAfterPredefinedAndRestoredDnaBeforeModifierConfiguration(bool restoreDNA)
        {
            var root = Keep(new GameObject("RecipePrepared test"));
            root.SetActive(false);
            var avatar = root.AddComponent<RecipeTestAvatar>();
            avatar.umaRecipe = new UMAData.UMARecipe();
            var race = Keep(ScriptableObject.CreateInstance<RaceData>());
            race.name = "OverrideTestRace";
            race.useNewDNA = false;
            avatar.activeRace.name = race.raceName;
            avatar.activeRace.data = race;
            var utility = MakeSlot("Overrides");
            var body = MakeSlot("Body");
            body.meshData = new UMAMeshData { vertexCount = 1, vertices = new[] { Vector3.zero } };
            var settings = root.AddComponent<UMADnaOverrideSlot>();
            settings.dna.Add(new UMADnaOverrideSlot.NamedValue { name = "height", value = 0.8f });
            settings.blendshapes.Add(new UMADnaOverrideSlot.NamedValue { name = "Shape", value = 0.25f });
            int calls = 0;
            utility.RecipePrepared.AddListener(data =>
            {
                calls++;
                Assert.That(data.umaRecipe.GetAllDna()[0].GetValue(0), Is.EqualTo(restoreDNA ? 0.45f : 0.35f));
            });
            utility.RecipePrepared.AddListener(settings.OnRecipePrepared);
            var recipe = Keep(ScriptableObject.CreateInstance<RecipeTestRecipe>());
            recipe.race = race;
            recipe.slots = new[] { new SlotData(utility), new SlotData(body) };
            var modifier = Keep(ScriptableObject.CreateInstance<MeshModifier>());
            modifier.runtimeModifiers.Add(new MeshModifier.Modifier { SlotName = "Body", DNAName = "height", Scale = 0.1f });
            recipe.MeshModifiers.Add(modifier);
            avatar.RecipeUpdated = new UMADataEvent();
            avatar.RecipeUpdated.AddListener(data => data.umaRecipe.GetAllDna()[0].SetValue(0, 0.2f));
            avatar.predefinedDNA = new UMAPredefinedDNA();
            avatar.predefinedDNA.AddDNA("height", 0.35f);
            var restored = new OverrideTestDNA();
            restored.SetValue(0, 0.45f);
            typeof(DynamicCharacterAvatar).GetMethod("LoadCharacter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(avatar,
                new object[] { recipe, new List<UMAWardrobeRecipe>(), new List<UMATextRecipe>(), Array.Empty<UMARecipeBase>(),
                    new Dictionary<string, List<MeshHideAsset>>(), new List<string>(), new List<string>(), new UMADnaBase[] { restored }, restoreDNA, true });
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(avatar.GetDNA()["height"].Value, Is.EqualTo(0.8f));
            Assert.That(avatar.Modifiers["Body"][0].Scale, Is.EqualTo(0.8f));
            Assert.That(avatar.blendShapeSettings.blendShapes["Shape"].isBaked, Is.True);
            Assert.That(avatar.blendShapeSettings.blendShapes["Shape"].value, Is.EqualTo(0.25f));
        }

        [Test]
        public void OverridesPreserveUnrelatedBakesAndLoadingPolicyAndSupportNewDna()
        {
            var root = Keep(new GameObject("Overrides"));
            var data = root.AddComponent<UMAData>();
            var settings = root.AddComponent<UMADnaOverrideSlot>();
            data.umaRecipe = new UMAData.UMARecipe();
            data.umaRecipe.dnaInstanceCollection = new DNAInstanceCollection();
            var dna = new DNAInstance("height", 0.3f, null);
            data.umaRecipe.dnaInstanceCollection.dnaInstances.Add(dna);
            data.blendShapeSettings.ignoreBlendShapes = true;
            var old = new BlendShapeData { isBaked = true, value = 0.15f };
            data.blendShapeSettings.blendShapes["Unrelated"] = old;
            settings.dna.Add(new UMADnaOverrideSlot.NamedValue { name = "height", value = 0.7f });
            settings.blendshapes.Add(new UMADnaOverrideSlot.NamedValue { name = "First", value = 0.4f });
            settings.blendshapes.Add(new UMADnaOverrideSlot.NamedValue { name = "Second", value = 0.9f });
            settings.OnRecipePrepared(data);
            settings.OnRecipePrepared(data);
            Assert.That(dna.Value, Is.EqualTo(0.7f));
            Assert.That(data.blendShapeSettings.blendShapes.Count, Is.EqualTo(3));
            Assert.That(data.blendShapeSettings.blendShapes["Unrelated"], Is.SameAs(old));
            Assert.That(data.blendShapeSettings.blendShapes["First"].value, Is.EqualTo(0.4f));
            Assert.That(data.blendShapeSettings.blendShapes["Second"].value, Is.EqualTo(0.9f));
            Assert.That(data.blendShapeSettings.ignoreBlendShapes, Is.True);
        }

        [Test]
        public void EventDispatchSkipsSuppressedAndMissingSlots()
        {
            var root = Keep(new GameObject("Dispatch"));
            var data = root.AddComponent<UMAData>();
            var first = MakeSlot("First");
            var second = MakeSlot("Second");
            var calls = new List<string>();
            first.RecipePrepared.AddListener(_ => calls.Add("First"));
            second.RecipePrepared.AddListener(_ => calls.Add("Second"));
            data.umaRecipe = new UMAData.UMARecipe { slotDataList = new[] { new SlotData(first) { Suppressed = true }, null, new SlotData(second) } };
            data.FireRecipePreparedEvents();
            Assert.That(calls, Is.EqualTo(new[] { "Second" }));
        }

        [Test]
        public void WeightedBonePoseAppliesToTheProvidedCharactersSkeleton()
        {
            var root = Keep(new GameObject("root"));
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform, false);
            var data = root.AddComponent<UMAData>();
            data.skeleton = new UMASkeleton(root.transform);
            var settings = root.AddComponent<UMADnaOverrideSlot>();
            var pose = Keep(ScriptableObject.CreateInstance<UMABonePose>());
            pose.poses = new[] { new UMABonePose.PoseBone { bone = "Child", hash = UMAUtils.StringToHash("Child"), position = Vector3.up, scale = Vector3.one, rotation = Quaternion.identity } };
            settings.bonePoses.Add(new UMADnaOverrideSlot.BonePoseValue { pose = pose, weight = 0.25f });
            settings.OnDnaApplied(data);
            Assert.That(child.transform.localPosition.y, Is.EqualTo(0.25f).Within(0.00001f));
        }

        [UnityTest]
        public IEnumerator InspectorBindsThreeReorderableCollapsibleListsAndSupportsUndo()
        {
            var root = Keep(new GameObject("Inspector"));
            var settings = root.AddComponent<UMADnaOverrideSlot>();
            settings.dna.Add(new UMADnaOverrideSlot.NamedValue { name = "height", value = 0.6f });
            var editor = Keep(Editor.CreateEditor(settings));
            var window = Keep(ScriptableObject.CreateInstance<OverrideTestWindow>());
            window.rootVisualElement.Add(editor.CreateInspectorGUI());
            window.Show();
            yield return null;
            yield return null;
            var lists = window.rootVisualElement.Query<ListView>().ToList();
            Assert.That(lists.Count, Is.EqualTo(3));
            Assert.That(lists[1].itemsSource.Count, Is.EqualTo(1));
            foreach (var list in lists) Assert.That(list.reorderable && list.showAddRemoveFooter, Is.True);
            Assert.That(window.rootVisualElement.Query<Foldout>().ToList().Count, Is.GreaterThanOrEqualTo(3));
            using (var state = new SerializedObject(settings))
            {
                state.FindProperty("dna").GetArrayElementAtIndex(0).FindPropertyRelative("value").floatValue = 0.9f;
                state.ApplyModifiedProperties();
                Assert.That(settings.dna[0].value, Is.EqualTo(0.9f));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.That(settings.dna[0].value, Is.EqualTo(0.6f));
            }
            window.Close();
        }

        [UnityTest]
        public IEnumerator ReferenceCharacterSurvivesInspectorRecreationBetweenAdditions()
        {
            var root = Keep(new GameObject("Reference persistence"));
            var settings = root.AddComponent<UMADnaOverrideSlot>();
            var character = root.AddComponent<UMAData>();
            character.umaRecipe = new UMAData.UMARecipe { dnaInstanceCollection = new DNAInstanceCollection() };
            character.umaRecipe.dnaInstanceCollection.dnaInstances.Add(new DNAInstance("height", 0.73f, null));
            var pose = Keep(ScriptableObject.CreateInstance<UMABonePose>());
            var editor = Keep(Editor.CreateEditor(settings));
            var ui = editor.CreateInspectorGUI();
            var window = Keep(ScriptableObject.CreateInstance<OverrideTestWindow>());
            window.rootVisualElement.Add(ui);
            window.Show();
            yield return null;
            var previousReference = ui.Q<ObjectField>("referenceCharacter").value;
            try
            {
                ui.Q<ObjectField>("referenceCharacter").value = character;
                foreach (var field in new[] { "blendshapes", "dna", "bonePoses" })
                {
                    typeof(UMADnaOverrideSlotEditor).GetMethod("AddEntry", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(editor, new object[] { field, "height", field == "bonePoses" ? pose : null, 0.5f });
                    ui.Unbind();
                    window.rootVisualElement.Clear();
                    Object.DestroyImmediate(editor);
                    editor = Keep(Editor.CreateEditor(settings));
                    ui = editor.CreateInspectorGUI();
                    window.rootVisualElement.Add(ui);
                    yield return null;
                    Assert.That(ui.Q<ObjectField>("referenceCharacter").value, Is.SameAs(character), field);
                }
                var dnaMenu = ui.Query<ToolbarMenu>().ToList()[1];
                var heightAction = dnaMenu.menu.MenuItems().OfType<DropdownMenuAction>().Single(action => action.name == "height");
                heightAction.Execute();
                Assert.That(settings.dna[1].value, Is.EqualTo(0.73f), "The dropdown must still copy the selected character's DNA value.");
                ui.Q<ObjectField>("referenceCharacter").value = null;
                ui.Unbind();
                window.rootVisualElement.Clear();
                Object.DestroyImmediate(editor);
                editor = Keep(Editor.CreateEditor(settings));
                ui = editor.CreateInspectorGUI();
                window.rootVisualElement.Add(ui);
                yield return null;
                Assert.That(ui.Q<ObjectField>("referenceCharacter").value, Is.Null, "Explicit clearing must survive recreation too.");
            }
            finally
            {
                ui.Q<ObjectField>("referenceCharacter").value = previousReference;
                ui.Unbind();
                window.Close();
            }
        }

        [Test]
        public void ExampleSlotHasPersistentHandlersOnItsSettingsPrefab()
        {
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>("Assets/UMA/SRP/Samples/UtilitySlots/DnaOverrides/DnaOverridesExample_slot.asset");
            Assert.That(slot, Is.Not.Null);
            Assert.That(slot.isUtilitySlot, Is.True);
            Assert.That(slot.RecipePrepared.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(slot.DNAApplied.GetPersistentEventCount(), Is.EqualTo(1));
            var settings = slot.RecipePrepared.GetPersistentTarget(0) as UMADnaOverrideSlot;
            Assert.That(settings, Is.Not.Null);
            Assert.That(slot.RecipePrepared.GetPersistentMethodName(0), Is.EqualTo("OnRecipePrepared"));
            Assert.That(slot.DNAApplied.GetPersistentTarget(0), Is.SameAs(settings));
            Assert.That(settings.blendshapes.Count, Is.EqualTo(1));
            Assert.That(settings.dna.Count, Is.EqualTo(1));
            Assert.That(settings.bonePoses.Count, Is.EqualTo(1));
            var root = Keep(new GameObject("Example invocation"));
            var data = root.AddComponent<UMAData>();
            data.umaRecipe = new UMAData.UMARecipe { slotDataList = new[] { new SlotData(slot) }, dnaInstanceCollection = new DNAInstanceCollection() };
            var height = new DNAInstance("height", 0.2f, null);
            data.umaRecipe.dnaInstanceCollection.dnaInstances.Add(height);
            data.FireRecipePreparedEvents();
            Assert.That(height.Value, Is.EqualTo(0.6f), "Example must also invoke its persistent handler during editor builds.");
            Assert.That(data.blendShapeSettings.blendShapes["OrcEars"].value, Is.EqualTo(0.25f));
        }

        public sealed class RecipeTestAvatar : DynamicCharacterAvatar { public override void Dirty() { } }
        public sealed class OverrideTestWindow : EditorWindow { }
        public sealed class RecipeTestRecipe : UMATextRecipe
        {
            public RaceData race;
            public SlotData[] slots;
            public override void Load(UMAData.UMARecipe recipe, RaceData raceData)
            {
                recipe.raceData = race;
                recipe.slotDataList = slots;
                recipe.ClearDna();
                recipe.AddDna(new OverrideTestDNA());
            }
        }
        [Serializable]
        public sealed class OverrideTestDNA : UMADnaBase
        {
            private float value = 0.5f;
            public override float[] Values { get => new[] { value }; set => this.value = value[0]; }
            public override string[] Names => new[] { "height" };
            public override int Count => 1;
            public override int DNATypeHash { get; set; } = 535001;
            public override float GetValue(int index) => value;
            public override void SetValue(int index, float input) => value = input;
        }
    }
}
