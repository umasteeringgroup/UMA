using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UMA.CharacterSystem.Editors;
using UMA.Dynamics;
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
        private bool originalCustomization, originalDna, originalUmaData;
        private int originalColorFilter;
        private bool originalPlayOptionsEnabled;
        private EnterPlayModeOptions originalPlayOptions;
        private DynamicCharacterAvatar avatar;
        private RaceData race;
        private bool hadViewPreference, originalAdvancedView;

        [Test]
        public void MissingRaceDoesNotGrowOnRepeatedDrawsOrSwitches()
        {
            var drawer = new RaceSetterPropertyDrawer();
            var ensure = typeof(RaceSetterPropertyDrawer).GetMethod("EnsureRaceIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            drawer.SetRaceLists(new[] { race });
            for (int i = 0; i < 1000; i++) ensure.Invoke(drawer, new object[] { "Unavailable" });
            Assert.That(drawer.foundRaceNames.Count, Is.EqualTo(3));
            ensure.Invoke(drawer, new object[] { "Another Missing Race" });
            Assert.That(drawer.foundRaceNames.Count, Is.EqualTo(3));
            ensure.Invoke(drawer, new object[] { race.raceName });
            Assert.That(drawer.foundRaceNames.Count, Is.EqualTo(2));
            // A same-size list replacement must not leave stale names/references behind.
            var other = Create<RaceData>();
            other.name = "Replacement Race";
            drawer.SetRaceLists(new[] { other });
            Assert.That(drawer.foundRaceNames[1], Is.EqualTo(other.raceName));
            drawer.SetRaceLists(null);
            Assert.That(drawer.foundRaceNames, Is.EqualTo(new[] { "None Set" }));
        }

        [Test]
        public void IdleWardrobeReusesMenusAndExplicitRefreshSeesChangedSlots()
        {
            var drawer = new WardrobeRecipeListPropertyDrawer { thisDCA = avatar };
            var ensure = drawer.GetType().GetMethod("EnsureDropdown", BindingFlags.Instance | BindingFlags.NonPublic);
            var labels = drawer.GetType().GetField("recipeMenuLabels", BindingFlags.Instance | BindingFlags.NonPublic);
            var count = drawer.GetType().GetField("dropdownRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic);
            ensure.Invoke(drawer, new object[] { race.raceName, race, false });
            var original = labels.GetValue(drawer);
            for (int i = 0; i < 100; i++) ensure.Invoke(drawer, new object[] { race.raceName, race, false });
            Assert.That(count.GetValue(drawer), Is.EqualTo(1));
            Assert.That(labels.GetValue(drawer), Is.SameAs(original));
            race.wardrobeSlots.Add("NewSlot");
            drawer.SetupDropdown(race.raceName, race);
            var slots = (string[])drawer.GetType().GetField("wardrobeSlotLabels", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(drawer);
            CollectionAssert.Contains(slots, "NewSlot");
            Assert.That(count.GetValue(drawer), Is.EqualTo(2));
        }

        [Test]
        public void DnaViewTracksSameCountReplacementAndKeepsValuesLive()
        {
            ConfigureNewDna(3);
            var editor = NewInspector();
            Invoke(editor, "EnsureDNACaches", race.DNACollection);
            var instances = avatar.dnaInstanceCollection.dnaInstances;
            Invoke(editor, "EnsureAssignedDNACache", instances);
            var snapshot = (List<DNAInstance>)Field(editor, "_dnaInstanceSnapshot");
            var first = snapshot[0];
            instances[0].Value = 0.9f;
            for (int i = 0; i < 100; i++) Invoke(editor, "EnsureAssignedDNACache", instances);
            Assert.That(Field(editor, "_dnaViewRebuildCount"), Is.EqualTo(1));
            Assert.That(first.Value, Is.EqualTo(0.9f));
            instances[0] = new DNAInstance("Replacement", 0.3f, null);
            Invoke(editor, "EnsureAssignedDNACache", instances);
            Assert.That(Field(editor, "_dnaViewRebuildCount"), Is.EqualTo(2));
            Assert.That(snapshot[0], Is.SameAs(instances[0]));
            instances[1].Name = "Renamed";
            Invoke(editor, "EnsureAssignedDNACache", instances);
            Assert.That(Field(editor, "_dnaViewRebuildCount"), Is.EqualTo(3));
            Assert.That(snapshot.Count, Is.EqualTo(3));
        }

        [Test]
        public void DnaMetadataDetectsSameCountRenameAndReplacement()
        {
            ConfigureNewDna(2);
            var editor = NewInspector();
            Invoke(editor, "EnsureDNACaches", race.DNACollection);
            var group = race.DNACollection.DNAGroups[0];
            var replacement = Create<DNA>();
            replacement.name = "New DNA";
            group.dnaList[0] = replacement;
            group.DNAArea = "Renamed Group";
            InvalidateCaches();
            Invoke(editor, "EnsureDNACaches", race.DNACollection);
            var cache = (Dictionary<string, DNA>)Field(editor, "_nameToDnaCache");
            Assert.That(cache["New DNA"], Is.SameAs(replacement));
            Assert.That(cache.ContainsKey("TestDna0"), Is.False);
            Assert.That(((string[])Field(editor, "_groupNamesCache"))[0], Is.EqualTo("Renamed Group"));
        }

        [UnityTest]
        public IEnumerator IdleLayoutDoesNotRefreshUntilDirtyOrInvalidated()
        {
            var editor = NewInspector();
            var window = NewWindow(editor);
            yield return WaitFor(() => window.Repaints > 1, window);
            FreezeRefresh(editor);
            int refreshes = (int)Field(editor, "_serializedRefreshCount");
            int repaints = window.Repaints;
            yield return WaitFor(() => window.Repaints >= repaints + 10, window);
            Assert.That(Field(editor, "_serializedRefreshCount"), Is.EqualTo(refreshes));
            Assert.That(Field(editor, "innerEditor"), Is.Null, "Collapsed UMA Data must not create a second Inspector.");
            avatar.userInformation = "External edit";
            EditorUtility.SetDirty(avatar);
            yield return WaitFor(() => (int)Field(editor, "_serializedRefreshCount") > refreshes, window);
            Assert.That(editor.serializedObject.FindProperty("userInformation").stringValue, Is.EqualTo("External edit"));
            FreezeRefresh(editor);
            refreshes = (int)Field(editor, "_serializedRefreshCount");
            avatar.userInformation = "Unmarked runtime edit";
            InvalidateCaches();
            yield return WaitFor(() => (int)Field(editor, "_serializedRefreshCount") > refreshes, window);
            Assert.That(editor.serializedObject.FindProperty("userInformation").stringValue, Is.EqualTo("Unmarked runtime edit"));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator UnmarkedExternalEditsAreEventuallyRefreshed()
        {
            var editor = NewInspector();
            var window = NewWindow(editor);
            yield return WaitFor(() => window.Repaints > 0, window);
            avatar.userInformation = "Direct runtime write";
            yield return WaitFor(() => editor.serializedObject.FindProperty("userInformation").stringValue == "Direct runtime write", window);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator UndoRefreshesAFrozenInspector()
        {
            var editor = NewInspector();
            var window = NewWindow(editor);
            avatar.userInformation = "Before Undo";
            yield return WaitFor(() => window.Repaints > 0, window);
            Undo.RecordObject(avatar, "Inspector regression edit");
            avatar.userInformation = "After edit";
            Undo.FlushUndoRecordObjects();
            EditorUtility.SetDirty(avatar);
            yield return WaitFor(() => editor.serializedObject.FindProperty("userInformation").stringValue == "After edit", window);
            FreezeRefresh(editor);
            Undo.PerformUndo();
            yield return WaitFor(() => editor.serializedObject.FindProperty("userInformation").stringValue == "Before Undo", window);
            Undo.ClearUndo(avatar);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ColorFoldoutDoesNotSignalAMaterialEdit()
        {
            var window = NewWindow(null);
            window.Unscrolled = true;
            window.Focus();
            var foldout = typeof(OverlayColorDataPropertyDrawer).GetMethod("ViewFoldout",
                BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(bool), typeof(GUIContent) }, null);
            bool open = false, materialChanged = false;
            Rect rect = default;
            string events = "";
            window.DrawExtra = () =>
            {
                if (Event.current.isMouse) events += $" {Event.current.type}@{Event.current.mousePosition},enabled={GUI.enabled}";
                EditorGUI.BeginChangeCheck();
                open = (bool)foldout.Invoke(null, new object[] { open, new GUIContent("Color controls") });
                if (Event.current.type == EventType.Repaint) rect = GUILayoutUtility.GetLastRect();
                materialChanged |= EditorGUI.EndChangeCheck();
            };
            yield return WaitFor(() => window.Repaints > 0, window);
            var mouse = rect.center; // toggleOnLabelClick: avoid platform-dependent arrow inset.
            int repaints = window.Repaints;
            // SendEvent is not dispatched to batch-mode EditorWindows on Windows. Feed the
            // mouse events inside a real OnGUI context, retaining Unity's layout/control state.
            window.InjectedEvent = new Event { type = EventType.MouseDown, button = 0, mousePosition = mouse };
            yield return WaitFor(() => window.Repaints > repaints, window);
            repaints = window.Repaints;
            window.InjectedEvent = new Event { type = EventType.MouseUp, button = 0, mousePosition = mouse };
            yield return WaitFor(() => window.Repaints > repaints, window);
            Assert.That(open, Is.True, $"Foldout rect {rect}; events:{events}");
            Assert.That(materialChanged, Is.False, "Opening color controls must not regenerate the avatar.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator InspectorIdleDrawBenchmark()
        {
            avatar.LoadAvatarDefinition(Definition(12));
            ConfigureNewDna(120);
            var window = NewWindow(NewInspector());
            yield return WaitFor(() => window.Repaints >= 5, window);
            window.GuiTicks = window.GuiEvents = 0;
            int before = window.Repaints;
            yield return WaitFor(() => window.Repaints >= before + 30, window);
            string measurement = $"DCA_INSPECTOR_BENCHMARK events={window.GuiEvents} ms/event=" +
                $"{window.GuiTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / window.GuiEvents:F3}";
            LogAssert.Expect(LogType.Log, measurement);
            Debug.Log(measurement);
            LogAssert.NoUnexpectedReceived();
        }

        private void ConfigureNewDna(int count)
        {
            race.useNewDNA = true;
            race.DNACollection = new DNACollection();
            avatar.umaRecipe = new UMAData.UMARecipe();
            avatar.dnaInstanceCollection = new DNAInstanceCollection();
            DNAGroup group = null;
            for (int i = 0; i < count; i++)
            {
                if (i % 10 == 0)
                {
                    group = Create<DNAGroup>();
                    group.DNAArea = "Group " + i;
                    group.editorFoldout = true;
                    race.DNACollection.DNAGroups.Add(group);
                }
                var dna = Create<DNA>();
                dna.name = "TestDna" + i;
                group.dnaList.Add(dna);
                avatar.dnaInstanceCollection.dnaInstances.Add(new DNAInstance(dna.name, 0.5f, group));
            }
        }

        private static void InvalidateCaches() => typeof(DynamicCharacterAvatarEditor).Assembly
            .GetType("UMA.CharacterSystem.Editors.AvatarInspectorCacheEpoch")
            .GetMethod("Invalidate", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        private static void FreezeRefresh(object editor)
        {
            var gate = Field(editor, "_serializedRefresh");
            gate.GetType().GetField("nextRefresh", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(gate, double.PositiveInfinity);
        }

        [SetUp]
        public void SetUp()
        {
            string viewKey = UMAInspectorView.PreferenceKey(typeof(DynamicCharacterAvatar));
            hadViewPreference = EditorPrefs.HasKey(viewKey);
            originalAdvancedView = EditorPrefs.GetBool(viewKey);
            // Existing regression cases exercise legacy and diagnostic controls, too.
            EditorPrefs.SetBool(viewKey, true);
            originalIndexer = IndexerField.GetValue(null);
            originalSettings = UMASettings.instance;
            originalSelection = Selection.activeObject;
            originalCustomization = DynamicCharacterAvatarEditor.showEditorCustomization;
            originalDna = DynamicCharacterAvatarEditor.showPrefinedDNA;
            originalUmaData = DynamicCharacterAvatarEditor.showUMAData;
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
            DynamicCharacterAvatarEditor.showUMAData = false;
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
            DynamicCharacterAvatarEditor.showUMAData = originalUmaData;
            DynamicCharacterAvatarEditor.currentcolorfilter = originalColorFilter;
            EditorSettings.enterPlayModeOptionsEnabled = originalPlayOptionsEnabled;
            EditorSettings.enterPlayModeOptions = originalPlayOptions;
            string viewKey = UMAInspectorView.PreferenceKey(typeof(DynamicCharacterAvatar));
            if (hadViewPreference) EditorPrefs.SetBool(viewKey, originalAdvancedView);
            else EditorPrefs.DeleteKey(viewKey);
        }

        [UnityTest]
        public IEnumerator StandardViewSurvivesDefinitionLoadsAndViewSwitches()
        {
            ConfigureNewDna(3);
            var window = NewWindow(NewInspector());
            foreach (bool advanced in new[] { false, true, false })
            {
                EditorPrefs.SetBool(UMAInspectorView.PreferenceKey(typeof(DynamicCharacterAvatar)), advanced);
                int before = window.Repaints;
                yield return WaitFor(() => window.Repaints >= before + 3, window);
                int aborts = window.AbortedEvents;
                before = window.Repaints;
                window.BeforeRepaint = () => avatar.LoadAvatarDefinition(Definition(3));
                yield return WaitFor(() => window.AbortedEvents > aborts && window.Repaints > before, window);
                Assert.That(avatar.dnaInstanceCollection.dnaInstances[0].Value, Is.EqualTo(.7f).Within(.0001f));
                LogAssert.NoUnexpectedReceived();
            }
        }

        [UnityTest]
        public IEnumerator StandardAssetInspectorsDrawWithoutChangingTheirAssets()
        {
            var slot = Create<SlotDataAsset>();
            slot.name = "Standard View Slot";
            var overlay = Create<OverlayDataAsset>();
            overlay.name = "Standard View Overlay";
            foreach (Object asset in new Object[] { slot, overlay, race })
            {
                string key = UMAInspectorView.PreferenceKey(asset.GetType());
                bool hadKey = EditorPrefs.HasKey(key), original = EditorPrefs.GetBool(key);
                EditorPrefs.DeleteKey(key);
                var inspector = UnityEditor.Editor.CreateEditor(asset);
                objects.Add(inspector);
                var window = NewWindow(null);
                window.position = new Rect(50, 50, 350, 650);
                window.DrawExtra = inspector.OnInspectorGUI;
                string before = EditorJsonUtility.ToJson(asset);
                try
                {
                    yield return WaitFor(() => window.Repaints >= 3, window);
                    Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(before), asset.GetType().Name);
                    Assert.That(EditorPrefs.HasKey(key), Is.False, "Merely viewing an asset must not persist a preference.");
                    LogAssert.NoUnexpectedReceived();
                }
                finally
                {
                    window.Close();
                    if (hadKey) EditorPrefs.SetBool(key, original); else EditorPrefs.DeleteKey(key);
                }
            }
        }

        [UnityTest]
        public IEnumerator UpdatedUMAInspectorsDrawStandardAndAdvancedViews()
        {
            var physicsElement = Create<UMAPhysicsElement>();
            var expressionGroup = Create<UMAExpressionGroup>();
            var dna = Create<DNA>();
            var dnaGroup = Create<DNAGroup>();
            var colors = Create<SharedColorTable>();
            var collection = Create<UMAWardrobeCollection>();
            var componentRoot = new GameObject("Updated Inspector Tests");
            componentRoot.SetActive(false);
            objects.Add(componentRoot);
            var rendererManager =
                componentRoot.AddComponent<DCARendererManager>();
            var physicsAvatar =
                componentRoot.AddComponent<UMAPhysicsAvatar>();
            var physicsSlot =
                componentRoot.AddComponent<UMAPhysicsSlotDefinition>();
            var animator =
                componentRoot.AddComponent<UMAMaterialAnimator>();
            var generator = componentRoot.AddComponent<UMAGenerator>();
            var generatorOverride =
                componentRoot.AddComponent<UMAGeneratorOverride>();

            Object[] inspected =
            {
                rendererManager, generator, colors, physicsAvatar,
                physicsElement, physicsSlot, expressionGroup, dna, dnaGroup,
                animator, collection, generatorOverride
            };
            Type[] preferenceTypes =
            {
                typeof(DCARendererManager), typeof(UMAGeneratorBuiltin),
                typeof(SharedColorTable), typeof(UMAPhysicsAvatar),
                typeof(UMAPhysicsElement), typeof(UMAPhysicsSlotDefinition),
                typeof(UMAExpressionGroup), typeof(DNA), typeof(DNAGroup),
                typeof(UMAMaterialAnimator), typeof(UMAWardrobeCollection),
                typeof(UMAGeneratorOverride)
            };

            for (int i = 0; i < inspected.Length; i++)
            {
                string key = UMAInspectorView.PreferenceKey(
                    preferenceTypes[i]);
                bool hadKey = EditorPrefs.HasKey(key);
                bool previous = EditorPrefs.GetBool(key);
                var inspector = UnityEditor.Editor.CreateEditor(inspected[i]);
                objects.Add(inspector);
                var window = NewWindow(null);
                window.position = new Rect(50, 50, 520, 780);
                window.DrawExtra = inspector.OnInspectorGUI;
                try
                {
                    foreach (bool advanced in new[] { false, true })
                    {
                        EditorPrefs.SetBool(key, advanced);
                        int before = window.Repaints;
                        yield return WaitFor(
                            () => window.Repaints >= before + 2, window);
                        LogAssert.NoUnexpectedReceived();
                    }
                }
                finally
                {
                    window.Close();
                    if (hadKey) EditorPrefs.SetBool(key, previous);
                    else EditorPrefs.DeleteKey(key);
                }
            }
        }

        [Test]
        public void StandardPropertiesSupportMultiObjectEditingAndUndo()
        {
            var first = Create<SlotDataAsset>();
            var second = Create<SlotDataAsset>();
            first.overlayScale = 1f;
            second.overlayScale = .5f;
            var view = new UMAInspectorView(typeof(SlotDataAsset));
            using var serialized = new SerializedObject(new Object[] { first, second });
            serialized.Update();
            var property = view.Property(serialized, "overlayScale");
            Assert.That(property.hasMultipleDifferentValues, Is.True);
            Assert.That(view.Property(serialized, "overlayScale"), Is.SameAs(property));
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            property.floatValue = .75f;
            serialized.ApplyModifiedProperties();
            Assert.That(first.overlayScale, Is.EqualTo(.75f));
            Assert.That(second.overlayScale, Is.EqualTo(.75f));
            Undo.RevertAllDownToGroup(group);
            Assert.That(first.overlayScale, Is.EqualTo(1f));
            Assert.That(second.overlayScale, Is.EqualTo(.5f));
        }

        [UnityTest]
        public IEnumerator StandardSectionsReserveFoldoutAndListBorderGutters()
        {
            var slot = Create<SlotDataAsset>();
            slot.tags = new[] { "Body", "Clothing" };
            using var serialized = new SerializedObject(slot);
            var view = new UMAInspectorView(typeof(SlotDataAsset));
            var tags = view.Property(serialized, "tags");
            var window = NewWindow(null);
            Rect panel = default, foldout = default, list = default;
            bool expanded = false;
            window.DrawExtra = () =>
            {
                using (UMAInspectorView.Section("Border regression"))
                {
                    EditorGUILayout.Foldout(expanded, "Foldout", true);
                    if (Event.current.type == EventType.Repaint) foldout = GUILayoutUtility.GetLastRect();
                    view.Field(serialized, "tags", "Tags");
                    if (Event.current.type == EventType.Repaint) list = GUILayoutUtility.GetLastRect();
                }
                if (Event.current.type == EventType.Repaint) panel = GUILayoutUtility.GetLastRect();
            };
            foreach (int width in new[] { 350, 900 })
            {
                window.position = new Rect(50, 50, width, 650);
                foreach (bool open in new[] { false, true })
                {
                    expanded = tags.isExpanded = open;
                    int before = window.Repaints;
                    yield return WaitFor(() => window.Repaints >= before + 3, window);
                    // Unity paints arrows/list chrome outside the allocated field rectangle.
                    // Include that overhang, not just the field itself, in the border check.
                    foreach (Rect field in new[] { foldout, list })
                    {
                        Assert.That(field.xMin - 18, Is.GreaterThanOrEqualTo(panel.xMin + 8),
                            $"Left border overlapped at width {width}, expanded={open}: {field} in {panel}");
                        Assert.That(field.xMax + 6, Is.LessThanOrEqualTo(panel.xMax - 8),
                            $"Right border overlapped at width {width}, expanded={open}: {field} in {panel}");
                    }
                    LogAssert.NoUnexpectedReceived();
                }
            }
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
        internal long GuiTicks, GuiEvents;
        internal bool Unscrolled;
        internal Event InjectedEvent;
        private Vector2 scroll;

        private void OnGUI()
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            bool repaint = Event.current.type == EventType.Repaint;
            var originalEvent = Event.current;
            if (repaint && BeforeRepaint != null)
            {
                var action = BeforeRepaint;
                BeforeRepaint = null;
                action();
            }
            try
            {
                if (repaint && InjectedEvent != null)
                {
                    Event.current = InjectedEvent;
                    InjectedEvent = null;
                }
                if (Unscrolled)
                {
                    if (Inspector != null) Inspector.OnInspectorGUI();
                    DrawExtra?.Invoke();
                }
                else using (var scrollView = new EditorGUILayout.ScrollViewScope(scroll))
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
            finally
            {
                Event.current = originalEvent;
                GuiTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                GuiEvents++;
            }
        }
    }
}
