using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public class PluginTestTarget : ScriptableObject { public int value; }
    public class PluginTestDerivedTarget : PluginTestTarget { }
    [CanEditMultipleObjects]
    public class PluginTestEditor : Editor { }

    public class UMAPluginTests
    {
        private readonly List<Object> objects = new();
        private static readonly List<string> calls = new();
        private static bool enabled;

        private sealed class Host : IUMAPluginHost
        {
            public bool CanRunPluginActions { get; set; } = true;
            public bool prepare = true;
            public bool TryPreparePluginAction() { calls.Add("prepare"); return prepare; }
            public void RefreshAfterPluginAction() { calls.Add("refresh"); }
        }

        [SetUp] public void SetUp() { calls.Clear(); enabled = true; }
        [TearDown] public void TearDown()
        {
            UMAPluginGUI.CancelPending();
            foreach (Object item in objects) if (item != null) Object.DestroyImmediate(item);
            objects.Clear();
        }

        private T New<T>() where T : ScriptableObject
        {
            var value = ScriptableObject.CreateInstance<T>(); objects.Add(value); return value;
        }
        private Editor EditorFor(params Object[] targets)
        {
            Editor editor = Editor.CreateEditor(targets, typeof(PluginTestEditor)); objects.Add(editor); return editor;
        }
        private static MethodInfo Method(string name) => typeof(UMAPluginTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        private static UMAPluginAction Action(string name = nameof(Invoke)) => UMAPluginRegistry.Create(Method(name));

        [UMAPlugin(typeof(PluginTestTarget), "Test action", ValidateMethod = nameof(Validate))]
        private static void Invoke(Editor editor)
        {
            calls.Add("invoke:" + ((PluginTestTarget)editor.target).value);
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(editor.target, "Plugin test change");
            ((PluginTestTarget)editor.target).value++;
        }
        private static bool Validate(Editor editor) => enabled;

        [UMAPlugin(typeof(PluginTestTarget), "Exact action", IncludeDerived = false)]
        private static void Exact(Editor editor) { }
        [UMAPlugin(typeof(PluginTestTarget), "Multiple action", SupportsMultipleTargets = true)]
        [UMAPlugin(typeof(PluginTestDerivedTarget), "Multiple action", SupportsMultipleTargets = true)]
        private static void Multiple(Editor editor) { }
        private static int Invalid(Editor editor) => 1;

        [Test] public void RegistrationRejectsWrongSignature() => Assert.Throws<ArgumentException>(() => Action(nameof(Invalid)));
        [Test] public void DerivedAndExactMatchingRespectRegistration()
        {
            var derived = New<PluginTestDerivedTarget>();
            Assert.That(Action().Matches(new Object[] { derived }), Is.True);
            Assert.That(Action(nameof(Exact)).Matches(new Object[] { derived }), Is.False);
        }
        [Test] public void MultiSelectionRequiresOptInAndAllTargetsMustMatch()
        {
            var editor = EditorFor(New<PluginTestTarget>(), New<PluginTestDerivedTarget>());
            Assert.That(Action().IsEnabled(editor), Is.False);
            Assert.That(Action(nameof(Multiple)).IsEnabled(editor), Is.True);
            Assert.That(Action(nameof(Multiple)).Matches(new Object[] { editor.target, New<UMASettings>() }), Is.False);
        }
        [Test] public void RepeatedRegistrationProducesOneDeterministicallyOrderedAction()
        {
            var actions = UMAPluginRegistry.Discover(new[] { Method(nameof(Invoke)), Method(nameof(Multiple)), Method(nameof(Multiple)), Method(nameof(Exact)) });
            Assert.That(actions.Length, Is.EqualTo(3));
            Assert.That(actions[0].Content.text, Is.EqualTo("Exact action"));
            Assert.That(actions[1].Content.text, Is.EqualTo("Multiple action"));
        }
        [UnityTest] public IEnumerator DefaultHostCommitsPendingSerializedEditsAndInvokesOnce()
        {
            var target = New<PluginTestTarget>(); var editor = EditorFor(target);
            editor.serializedObject.FindProperty("value").intValue = 42;
            Assert.That(UMAPluginGUI.Queue(editor, Action()), Is.True);
            Assert.That(UMAPluginGUI.Queue(editor, Action()), Is.False);
            for (int i = 0; i < 5 && calls.Count == 0; i++) yield return null;
            CollectionAssert.AreEqual(new[] { "invoke:42" }, calls);
            Assert.That(editor.serializedObject.FindProperty("value").intValue, Is.EqualTo(43));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(target.value, Is.EqualTo(42));
        }
        [UnityTest] public IEnumerator AdapterPreparesAndRefreshesAroundTheAction()
        {
            var editor = EditorFor(New<PluginTestTarget>()); var host = new Host();
            using var lease = UMAPluginGUI.RegisterExplicitHost(editor, host);
            Assert.That(UMAPluginGUI.Queue(editor, Action()), Is.True);
            for (int i = 0; i < 5 && calls.Count == 0; i++) yield return null;
            CollectionAssert.AreEqual(new[] { "prepare", "invoke:0", "refresh" }, calls);
        }
        [UnityTest] public IEnumerator DisposingOwnerCancelsDeferredAction()
        {
            var editor = EditorFor(New<PluginTestTarget>());
            var lease = UMAPluginGUI.RegisterExplicitHost(editor, new Host());
            Assert.That(UMAPluginGUI.Queue(editor, Action()), Is.True); lease.Dispose();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(calls, Is.Empty);
        }
        [UnityTest] public IEnumerator ValidatorIsCheckedAgainAfterQueueing()
        {
            var editor = EditorFor(New<PluginTestTarget>());
            Assert.That(UMAPluginGUI.Queue(editor, Action()), Is.True); enabled = false;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(calls, Is.Empty);
        }
        [UnityTest] public IEnumerator DestroyedEditorAndRegistryResetCancelPendingWork()
        {
            var editor = EditorFor(New<PluginTestTarget>());
            Assert.That(UMAPluginGUI.Queue(editor, Action()), Is.True); Object.DestroyImmediate(editor);
            var second = EditorFor(New<PluginTestTarget>());
            Assert.That(UMAPluginGUI.Queue(second, Action()), Is.True); UMAPluginRegistry.Reset();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(calls, Is.Empty);
        }
        [UnityTest] public IEnumerator HostCanDeclineWithoutInvokingOrRefreshing()
        {
            var editor = EditorFor(New<PluginTestTarget>());
            using var lease = UMAPluginGUI.RegisterExplicitHost(editor, new Host { prepare = false });
            Assert.That(UMAPluginGUI.Queue(editor, Action()), Is.True);
            for (int i = 0; i < 5 && calls.Count == 0; i++) yield return null;
            CollectionAssert.AreEqual(new[] { "prepare" }, calls);
        }
        [Test] public void OrderedTargetsArePartOfTheCapturedContext()
        {
            var first = New<PluginTestTarget>(); var second = New<PluginTestTarget>();
            Assert.That(UMAPluginGUI.SameTargets(new Object[] { first, second }, new Object[] { second, first }), Is.False);
        }
    }
}
