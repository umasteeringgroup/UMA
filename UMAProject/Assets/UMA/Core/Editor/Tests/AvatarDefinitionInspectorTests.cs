using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UMA.CharacterSystem.Editors;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class AvatarDefinitionInspectorTests
    {
        private static readonly FieldInfo IndexerField = typeof(UMAAssetIndexer).GetField(
            "theIndexer", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly List<Object> objects = new List<Object>();
        private object originalIndexer;
        private UMASettings originalSettings;
        private Object originalSelection;
        private bool originalCustomization, originalDna;
        private int originalColorFilter;
        private bool originalPlayOptionsEnabled;
        private EnterPlayModeOptions originalPlayOptions;
        private DynamicCharacterAvatar avatar;
        private RaceData race;

        [SetUp]
        public void SetUp()
        {
            originalIndexer = IndexerField.GetValue(null);
            originalSettings = UMASettings.instance;
            originalSelection = Selection.activeObject;
            originalCustomization = DynamicCharacterAvatarEditor.showEditorCustomization;
            originalDna = DynamicCharacterAvatarEditor.showPrefinedDNA;
            originalColorFilter = DynamicCharacterAvatarEditor.currentcolorfilter;
            originalPlayOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            originalPlayOptions = EditorSettings.enterPlayModeOptions;
            UMASettings.instance = Create<UMASettings>();
            UMASettings.instance.autoRepairIndex = false;
            var indexer = Create<UMAAssetIndexer>();
            IndexerField.SetValue(null, indexer);
            race = Create<RaceData>();
            race.name = "Inspector Regression Race";
            race.useNewDNA = false;
            indexer.SerializedItems.Clear();
            indexer.SerializedItems.Add(new AssetItem(typeof(RaceData), race.name, "", race));
            indexer.DoInitialDictionaryLoad();
            var go = new GameObject("Avatar Inspector Regression");
            go.SetActive(false); // No character generation or persistent project assets required.
            objects.Add(go);
            avatar = go.AddComponent<DynamicCharacterAvatar>();
            avatar.editorTimeGeneration = false;
            avatar.activeRace.name = race.raceName;
            avatar.activeRace.data = race;
            DynamicCharacterAvatarEditor.showEditorCustomization = true;
            DynamicCharacterAvatarEditor.showPrefinedDNA = true;
            DynamicCharacterAvatarEditor.currentcolorfilter = 1;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] is EditorWindow window) window.Close();
                else if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
            IndexerField.SetValue(null, originalIndexer);
            UMASettings.instance = originalSettings;
            Selection.activeObject = originalSelection;
            DynamicCharacterAvatarEditor.showEditorCustomization = originalCustomization;
            DynamicCharacterAvatarEditor.showPrefinedDNA = originalDna;
            DynamicCharacterAvatarEditor.currentcolorfilter = originalColorFilter;
            EditorSettings.enterPlayModeOptionsEnabled = originalPlayOptionsEnabled;
            EditorSettings.enterPlayModeOptions = originalPlayOptions;
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LoadIsSynchronousAndInvalidatesInspectorsOnce(bool useStringOverload)
        {
            var definition = Definition(3);
            uint before = avatar.EditorAvatarDefinitionRevision;
            if (useStringOverload) avatar.LoadAvatarDefinition(JsonUtility.ToJson(definition));
            else avatar.LoadAvatarDefinition(definition);
            Assert.That(avatar.EditorAvatarDefinitionRevision, Is.EqualTo(before + 1));
            Assert.That(avatar.characterColors.Colors.Count, Is.EqualTo(3));
            Assert.That(avatar.predefinedDNA.Count, Is.EqualTo(3));
            Assert.That(avatar.predefinedDNA.PreloadValues[0].Value, Is.EqualTo(0.7f).Within(0.0001));
            StringAssert.DoesNotContain("EditorAvatarDefinitionRevision", JsonUtility.ToJson(avatar));
        }

        [Test]
        public void PartiallyFailedLoadStillInvalidatesWithoutSwallowingException()
        {
            uint before = avatar.EditorAvatarDefinitionRevision;
            var definition = Definition(0);
            definition.Colors = new[] { new SharedColorDef("Broken", 1)
                { channels = new[] { new ColorDef(4, 0, 0) } } };
            Assert.Throws<IndexOutOfRangeException>(() => avatar.LoadAvatarDefinition(definition));
            Assert.That(avatar.EditorAvatarDefinitionRevision, Is.EqualTo(before + 1));
        }

        [Test]
        public void ClosingInspectorCancelsItsQueuedLoad()
        {
            var editor = NewInspector();
            uint before = avatar.EditorAvatarDefinitionRevision;
            Invoke(editor, "QueueAvatarDefinitionLoad", Definition(2));
            Assert.That(avatar.EditorAvatarDefinitionRevision, Is.EqualTo(before), "Loading must be deferred.");
            Assert.That(Field(editor, "_pendingAvatarLoad"), Is.Not.Null);
            editor.OnDisable();
            Assert.That(Field(editor, "_pendingAvatarLoad"), Is.Null);
            Invoke(editor, "RunPendingAvatarLoad");
            Assert.That(avatar.EditorAvatarDefinitionRevision, Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator ScriptedLoadsBetweenLayoutAndRepaintRecoverInIndependentInspectors()
        {
            var first = NewWindow(NewInspector());
            var second = NewWindow(NewInspector());
            Selection.activeObject = race; // Both editors remain bound to the avatar, like locked Inspectors.
            yield return WaitFor(() => first.Repaints > 0 && second.Repaints > 0, first, second);

            foreach (int count in new[] { 7, 1, 0, 4 })
            {
                int aborts = first.AbortedEvents;
                int repaint1 = first.Repaints, repaint2 = second.Repaints;
                bool loaded = false;
                first.BeforeRepaint = () => { avatar.LoadAvatarDefinition(Definition(count)); loaded = true; };
                yield return WaitFor(() => loaded && first.AbortedEvents > aborts &&
                    first.Repaints > repaint1 && second.Repaints > repaint2 &&
                    ObservedRevision(first.Inspector) == avatar.EditorAvatarDefinitionRevision &&
                    ObservedRevision(second.Inspector) == avatar.EditorAvatarDefinitionRevision, first, second);
                Assert.That(avatar.characterColors.Colors.Count, Is.EqualTo(count));
                Assert.That(avatar.predefinedDNA.Count, Is.EqualTo(count));
                foreach (var editor in new[] { first.Inspector, second.Inspector })
                    Assert.That(editor.serializedObject.FindProperty("characterColors._colors").arraySize,
                        Is.EqualTo(count), "Each Inspector must refresh its own serialized snapshot.");
                LogAssert.NoUnexpectedReceived();
            }
        }

        [UnityTest]
        public IEnumerator SwitchingRacesAndNewDnaCollectionsRefreshesTheInspector()
        {
            var legacyRace = race;
            var newRace = Create<RaceData>();
            newRace.name = "Inspector New DNA Race";
            newRace.useNewDNA = true;
            newRace.DNACollection = new DNACollection();
            var group = Create<DNAGroup>();
            group.DNAArea = "Regression DNA";
            group.editorFoldout = true;
            for (int i = 0; i < 3; i++)
            {
                var dna = Create<DNA>();
                dna.name = "TestDna" + i;
                group.dnaList.Add(dna);
            }
            newRace.DNACollection.DNAGroups.Add(group);
            var indexer = (UMAAssetIndexer)IndexerField.GetValue(null);
            indexer.SerializedItems.Add(new AssetItem(typeof(RaceData), newRace.name, "", newRace));
            indexer.DoInitialDictionaryLoad();
            avatar.umaRecipe = new UMAData.UMARecipe();
            var window = NewWindow(NewInspector());
            yield return WaitFor(() => window.Repaints > 0, window);

            foreach (var nextRace in new[] { newRace, legacyRace, newRace })
            {
                int aborts = window.AbortedEvents;
                int repaints = window.Repaints;
                race = nextRace;
                window.BeforeRepaint = () => avatar.LoadAvatarDefinition(Definition(3));
                yield return WaitFor(() => window.AbortedEvents > aborts && window.Repaints > repaints, window);
                Assert.That(avatar.activeRace.data, Is.SameAs(nextRace));
                Assert.That(window.Inspector.serializedObject.FindProperty("activeRace.name").stringValue,
                    Is.EqualTo(nextRace.raceName));
                if (nextRace.useNewDNA)
                {
                    Assert.That(Field(window.Inspector, "_cachedDNACollectionRef"), Is.SameAs(newRace.DNACollection));
                    Assert.That(avatar.dnaInstanceCollection.dnaInstances.Count, Is.EqualTo(3));
                    Assert.That(avatar.dnaInstanceCollection.dnaInstances[0].Value, Is.EqualTo(0.7f).Within(0.0001));
                }
                LogAssert.NoUnexpectedReceived();
            }
        }

        [UnityTest]
        public IEnumerator ScriptedLoadsAlsoRecoverWhilePlaying()
        {
            var testIndexer = IndexerField.GetValue(null);
            var testSettings = UMASettings.instance;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload |
                EnterPlayModeOptions.DisableSceneReload;
            yield return new EnterPlayMode(false);
            // SubsystemRegistration resets UMA's static services even without a domain reload.
            IndexerField.SetValue(null, testIndexer);
            UMASettings.instance = testSettings;
            DynamicCharacterAvatarEditor.showEditorCustomization = true;
            DynamicCharacterAvatarEditor.showPrefinedDNA = true;
            DynamicCharacterAvatarEditor.currentcolorfilter = 1;
            Assert.That(Application.isPlaying, Is.True);
            yield return ScriptedLoadsBetweenLayoutAndRepaintRecoverInIndependentInspectors();
        }

        [UnityTest]
        public IEnumerator PaddedGroupClosesOnEarlyReturnAndWardrobeMissingList()
        {
            var window = NewWindow(null);
            var editor = NewInspector();
            var drawer = new WardrobeRecipeListPropertyDrawer();
            var foldout = typeof(WardrobeRecipeListPropertyDrawer).GetField("defaultOpen",
                BindingFlags.Static | BindingFlags.NonPublic);
            bool previousFoldout = (bool)foldout.GetValue(null);
            foldout.SetValue(null, true);
            window.DrawExtra = () =>
            {
                DrawEarlyReturn();
                // A valid property of the wrong shape exercises the drawer's missing-list return.
                drawer.OnGUI(new Rect(), editor.serializedObject.FindProperty("activeRace"), GUIContent.none);
                GUILayout.Label("Control after both early returns");
            };
            try
            {
                yield return WaitFor(() => window.Repaints >= 3, window);
                LogAssert.NoUnexpectedReceived();
            }
            finally { foldout.SetValue(null, previousFoldout); }
        }

        private static void DrawEarlyReturn()
        {
            using var padded = new GUIHelper.PaddedVerticalScope(10, Color.white);
            using var row = new EditorGUILayout.HorizontalScope();
            GUILayout.Label("Early return");
            return;
        }

        private static IEnumerator WaitFor(Func<bool> condition, params AvatarInspectorTestWindow[] windows)
        {
            double deadline = EditorApplication.timeSinceStartup + 15;
            while (!condition() && EditorApplication.timeSinceStartup < deadline)
            {
                foreach (var window in windows) window.Repaint();
                yield return null;
            }
            Assert.That(condition(), Is.True, "The real Inspector GUI events did not complete.");
        }

        private DynamicCharacterAvatarEditor NewInspector()
        {
            var editor = (DynamicCharacterAvatarEditor)UnityEditor.Editor.CreateEditor(avatar);
            objects.Add(editor);
            editor.serializedObject.FindProperty("characterColors._colors").isExpanded = true;
            return editor;
        }

        private AvatarInspectorTestWindow NewWindow(DynamicCharacterAvatarEditor editor)
        {
            var window = Create<AvatarInspectorTestWindow>();
            window.Inspector = editor;
            window.position = new Rect(50, 50, 700, 900);
            window.Show();
            return window;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var value = ScriptableObject.CreateInstance<T>();
            objects.Add(value);
            return value;
        }

        private AvatarDefinition Definition(int count)
        {
            var definition = new AvatarDefinition { RaceName = race.raceName,
                Colors = new SharedColorDef[count], Dna = new DnaDef[count] };
            for (int i = 0; i < count; i++)
            {
                definition.Colors[i] = new SharedColorDef("TestColor" + i, 1)
                    { channels = new[] { new ColorDef(0, ColorDef.ToUInt(Color.cyan), 0) } };
                definition.Dna[i] = new DnaDef("TestDna" + i, 0.7f);
            }
            return definition;
        }

        private static object Field(object instance, string name) => typeof(DynamicCharacterAvatarEditor)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static uint ObservedRevision(object editor) => (uint)Field(editor, "_layoutDefinitionRevision");
        private static void Invoke(object instance, string name, params object[] arguments) =>
            typeof(DynamicCharacterAvatarEditor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(instance, arguments);
    }

    // A real IMGUI host: Unity must process Layout/Repaint and report unbalanced groups.
    public sealed class AvatarInspectorTestWindow : EditorWindow
    {
        internal DynamicCharacterAvatarEditor Inspector;
        internal Action BeforeRepaint, DrawExtra;
        internal int Repaints, AbortedEvents;
        private Vector2 scroll;

        private void OnGUI()
        {
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint && BeforeRepaint != null)
            {
                var action = BeforeRepaint;
                BeforeRepaint = null;
                action();
            }
            try
            {
                using (var scrollView = new EditorGUILayout.ScrollViewScope(scroll))
                {
                    scroll = scrollView.scrollPosition;
                    if (Inspector != null) Inspector.OnInspectorGUI();
                    DrawExtra?.Invoke();
                }
                if (repaint) Repaints++;
            }
            catch (ExitGUIException)
            {
                AbortedEvents++;
                throw; // Never suppress Unity's GUI control flow, even in a test host.
            }
        }
    }
}
