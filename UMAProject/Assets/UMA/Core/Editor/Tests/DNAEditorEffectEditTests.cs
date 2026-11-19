using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    /// <summary>
    /// DNA effects are edited in the DNA inspector directly, not through a SerializedObject. An edit (a curve,
    /// a mapping value) must mark the DNA asset dirty, or it stays in memory, is never saved, and a duplicate of
    /// the asset copies the old values from disk. Viewing an effect must not mark the asset dirty.
    /// </summary>
    public class DNAEditorEffectEditTests
    {
        [Serializable]
        private class EditedInInspectorEffect : DNAEffect
        {
            public float edited;
            public int drawn;

            public override void DoGui(bool showDescription, bool showHelp, out AnimationCurve curveToCopy)
            {
                curveToCopy = null;
                drawn++;
                // The curve field reports a change made in the curve editor window; the window has already
                // changed the curve itself, before this redraw (and before the inspector's Undo.RecordObject).
                GUI.changed = true;
            }
        }

        private class InspectorHost : EditorWindow
        {
            public Action draw;
            private void OnGUI() { draw?.Invoke(); }
        }

        private DNA dna;
        private Editor editor;
        private InspectorHost host;

        [SetUp]
        public void SetUp()
        {
            dna = ScriptableObject.CreateInstance<DNA>();
            dna.name = "DNAEditorEffectEditTests";
            dna.hideFlags = HideFlags.DontSave;
            editor = Editor.CreateEditor(dna);
            host = ScriptableObject.CreateInstance<InspectorHost>();
            host.titleContent = new GUIContent("DNAEditorEffectEditTests");
            host.position = new Rect(60, 60, 420, 700);
            host.draw = editor.OnInspectorGUI;
            host.Show();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) host.Close();
            if (editor != null) Object.DestroyImmediate(editor);
            if (dna != null) Object.DestroyImmediate(dna);
        }

        // Draws the window once, now (EditorWindow.RepaintImmediately is internal).
        private void DrawNow()
        {
            var repaint = typeof(EditorWindow).GetMethod("RepaintImmediately",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.That(repaint, Is.Not.Null, "EditorWindow.RepaintImmediately not found");
            repaint.Invoke(host, null);
        }

        [Test]
        public void EditingAnEffectMarksTheDNAAssetDirty()
        {
            Assert.That(editor, Is.InstanceOf<DNAEditor>());
            var effect = new EditedInInspectorEffect { expanded = true };
            dna.effects.Add(effect);
            int before = EditorUtility.GetDirtyCount(dna);
            // What the curve editor window does between redraws: it changes the effect's data in place.
            effect.edited = 0.75f;

            DrawNow();

            Assert.That(effect.drawn, Is.GreaterThan(0), "The effect's panel was not drawn.");
            Assert.That(EditorUtility.GetDirtyCount(dna), Is.GreaterThan(before),
                "An edit in an effect's panel must mark the DNA asset dirty so it is saved.");
        }

        [Test]
        public void ViewingACollapsedEffectDoesNotMarkTheDNAAssetDirty()
        {
            var effect = new EditedInInspectorEffect { expanded = false };
            dna.effects.Add(effect);
            int before = EditorUtility.GetDirtyCount(dna);

            DrawNow();

            Assert.That(effect.drawn, Is.EqualTo(0));
            Assert.That(EditorUtility.GetDirtyCount(dna), Is.EqualTo(before));
        }
    }
}
