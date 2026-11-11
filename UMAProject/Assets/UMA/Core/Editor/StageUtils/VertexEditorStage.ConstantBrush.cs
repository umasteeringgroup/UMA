using System;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Constant application for the sculpt and mask brushes: while the mouse button is held, the brush
    /// is applied again at a fixed interval, so its effect builds up without moving the pointer.
    /// </summary>
    /// <remarks>
    /// A normal stroke moves each vertex at most once to the brush's full effect, however long the
    /// pointer rests on it or however often it passes over; that is what keeps a stroke even. With
    /// constant application every interval starts a new dab at the pointer: the per-stroke limits
    /// start over and one sample is applied. The whole press is still one stroke and one Undo step.
    /// The anchored drag tools (Grab, Boundary, Elastic) are unaffected.
    /// </remarks>
    public partial class VertexEditorStage
    {
        private const string ConstantApplicationPrefsKey = "UMA.Sculpt.ConstantApplication";
        private const string ConstantIntervalPrefsKey = "UMA.Sculpt.ConstantApplicationIntervalMs";
        public const int DefaultConstantApplicationIntervalMs = 100;
        private const int MinConstantApplicationIntervalMs = 10;
        private const int MaxConstantApplicationIntervalMs = 2000;

        private bool sculptConstantApplication;
        private int sculptConstantIntervalMs = DefaultConstantApplicationIntervalMs;
        private bool sculptConstantPrefsLoaded;
        private double sculptNextConstantApplication;

        /// <summary>Whether the brush keeps applying while the mouse button is held.</summary>
        public bool SculptConstantApplication
        {
            get
            {
                LoadConstantApplicationPrefs();
                return sculptConstantApplication;
            }
            set
            {
                LoadConstantApplicationPrefs();
                sculptConstantApplication = value;
                EditorPrefs.SetBool(ConstantApplicationPrefsKey, value);
            }
        }

        /// <summary>Milliseconds between two applications of the brush while the button is held.</summary>
        public int SculptConstantApplicationIntervalMs
        {
            get
            {
                LoadConstantApplicationPrefs();
                return sculptConstantIntervalMs;
            }
            set
            {
                LoadConstantApplicationPrefs();
                sculptConstantIntervalMs = Mathf.Clamp(value, MinConstantApplicationIntervalMs, MaxConstantApplicationIntervalMs);
                EditorPrefs.SetInt(ConstantIntervalPrefsKey, sculptConstantIntervalMs);
            }
        }

        // Kept in EditorPrefs: a new stage object is made every time the editor opens.
        private void LoadConstantApplicationPrefs()
        {
            if (sculptConstantPrefsLoaded)
            {
                return;
            }
            sculptConstantPrefsLoaded = true;
            sculptConstantApplication = EditorPrefs.GetBool(ConstantApplicationPrefsKey, false);
            sculptConstantIntervalMs = Mathf.Clamp(EditorPrefs.GetInt(ConstantIntervalPrefsKey, DefaultConstantApplicationIntervalMs),
                MinConstantApplicationIntervalMs, MaxConstantApplicationIntervalMs);
        }

        private void DrawConstantApplicationOptions()
        {
            LoadConstantApplicationPrefs();
            using (new EditorGUI.DisabledScope(IsSculptDragMode))
            {
                bool constant = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Constant Application",
                        "Keep applying the brush while the mouse button is held, also when the pointer does not move, so the effect builds up. " +
                        "Off: one stroke moves each vertex at most once to the brush's full effect. Grab, Boundary and Elastic are not affected."),
                    sculptConstantApplication);
                if (constant != sculptConstantApplication)
                {
                    SculptConstantApplication = constant;
                }
                if (sculptConstantApplication)
                {
                    EditorGUI.indentLevel++;
                    int interval = EditorGUILayout.IntSlider(
                        new GUIContent("Every (ms)", "How often the brush is applied again while the button is held. Smaller builds up faster."),
                        sculptConstantIntervalMs, MinConstantApplicationIntervalMs, MaxConstantApplicationIntervalMs);
                    if (interval != sculptConstantIntervalMs)
                    {
                        SculptConstantApplicationIntervalMs = interval;
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }

        /// <summary>A stroke began: the first repeat comes one interval from now.</summary>
        private void ScheduleConstantSculptApplication()
        {
            LoadConstantApplicationPrefs();
            sculptNextConstantApplication = EditorApplication.timeSinceStartup + sculptConstantIntervalMs / 1000.0;
        }

        /// <summary>Applies the brush again at the pointer when constant application is on and an interval has passed.</summary>
        private void ApplyConstantSculptIfDue()
        {
            LoadConstantApplicationPrefs();
            if (!sculptConstantApplication || !sculpting || IsSculptDragMode || sculptGrabInitialized || !sculptHoverValid)
            {
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            if (now < sculptNextConstantApplication)
            {
                return;
            }
            // One application per frame at most: a slow frame does not cause a burst afterwards.
            sculptNextConstantApplication = now + sculptConstantIntervalMs / 1000.0;
            ApplyConstantSculptDab();
        }

        /// <summary>
        /// One repeat: the stroke's per-vertex limits start over, so this sample moves the vertices
        /// under the brush again, and one sample is applied at the pointer.
        /// </summary>
        private void ApplyConstantSculptDab()
        {
            if (!sculpting || sculptStrokeApplied == null || sculptStrokeLimit == null)
            {
                return;
            }
            Array.Clear(sculptStrokeApplied, 0, sculptStrokeApplied.Length);
            Array.Clear(sculptStrokeLimit, 0, sculptStrokeLimit.Length);
            sculptStrokeAppliedOtherSlots.Clear();
            sculptStrokeLimitOtherSlots.Clear();
            sculptMaskStrokeOtherSlots.Clear();
            ApplySculptSample(sculptHoverPoint, sculptHoverNormal);
        }
    }
}
