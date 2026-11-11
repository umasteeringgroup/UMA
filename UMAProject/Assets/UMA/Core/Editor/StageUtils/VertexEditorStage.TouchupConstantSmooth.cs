using System;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Touchup Weights: constant application for the Smooth paint brush.
    /// </summary>
    /// <remarks>
    /// A paint stroke changes each vertex once, however long the pointer rests on it. With constant
    /// application the Smooth brush is applied again at a fixed interval while the mouse button is
    /// held, so the weights keep evening out under the pointer without moving it, as the sculpt
    /// brushes do with their Constant Application. The whole press is still one stroke and one Undo
    /// step. The other paint operations are unaffected.
    /// </remarks>
    public partial class VertexEditorStage
    {
        private const string TouchupConstantSmoothPrefsKey = "UMA.TouchupWeights.ConstantSmooth";
        private const string TouchupConstantSmoothIntervalPrefsKey = "UMA.TouchupWeights.ConstantSmoothIntervalMs";
        public const int DefaultTouchupConstantSmoothIntervalMs = 100;
        private const int MinTouchupConstantSmoothIntervalMs = 10;
        private const int MaxTouchupConstantSmoothIntervalMs = 2000;
        private const string TouchupWeightStrokeKeyPrefix = "weight:";

        private bool touchupConstantSmooth;
        private int touchupConstantSmoothIntervalMs = DefaultTouchupConstantSmoothIntervalMs;
        private bool touchupConstantSmoothPrefsLoaded;
        private double touchupNextConstantSmooth;
        private int touchupConstantSmoothRepeats;

        /// <summary>Whether the Smooth paint brush keeps applying while the mouse button is held.</summary>
        internal bool TouchupConstantSmooth
        {
            get
            {
                LoadTouchupConstantSmoothPrefs();
                return touchupConstantSmooth;
            }
            set
            {
                LoadTouchupConstantSmoothPrefs();
                touchupConstantSmooth = value;
                EditorPrefs.SetBool(TouchupConstantSmoothPrefsKey, value);
            }
        }

        /// <summary>Milliseconds between two applications of the Smooth brush while the button is held.</summary>
        internal int TouchupConstantSmoothIntervalMs
        {
            get
            {
                LoadTouchupConstantSmoothPrefs();
                return touchupConstantSmoothIntervalMs;
            }
            set
            {
                LoadTouchupConstantSmoothPrefs();
                touchupConstantSmoothIntervalMs = Mathf.Clamp(value, MinTouchupConstantSmoothIntervalMs, MaxTouchupConstantSmoothIntervalMs);
                EditorPrefs.SetInt(TouchupConstantSmoothIntervalPrefsKey, touchupConstantSmoothIntervalMs);
            }
        }

        /// <summary>How many times the current or last stroke applied the brush again.</summary>
        internal int TouchupConstantSmoothRepeats => touchupConstantSmoothRepeats;

        // Kept in EditorPrefs: a new stage object is made every time the editor opens.
        private void LoadTouchupConstantSmoothPrefs()
        {
            if (touchupConstantSmoothPrefsLoaded)
            {
                return;
            }
            touchupConstantSmoothPrefsLoaded = true;
            touchupConstantSmooth = EditorPrefs.GetBool(TouchupConstantSmoothPrefsKey, false);
            touchupConstantSmoothIntervalMs = Mathf.Clamp(
                EditorPrefs.GetInt(TouchupConstantSmoothIntervalPrefsKey, DefaultTouchupConstantSmoothIntervalMs),
                MinTouchupConstantSmoothIntervalMs, MaxTouchupConstantSmoothIntervalMs);
        }

        /// <summary>Drawn with the Smooth brush's own options.</summary>
        private void DrawTouchupConstantSmoothOptions()
        {
            LoadTouchupConstantSmoothPrefs();
            bool constant = EditorGUILayout.Toggle(
                new GUIContent(
                    "Constant Application",
                    "Keep smoothing while the mouse button is held, also when the pointer does not move, so the weights keep evening out. " +
                    "Off: one stroke smooths each vertex once."),
                touchupConstantSmooth);
            if (constant != touchupConstantSmooth)
            {
                TouchupConstantSmooth = constant;
            }
            if (touchupConstantSmooth)
            {
                EditorGUI.indentLevel++;
                int interval = EditorGUILayout.IntSlider(
                    new GUIContent("Every (ms)", "How often the brush is applied again while the button is held. Smaller smooths faster."),
                    touchupConstantSmoothIntervalMs, MinTouchupConstantSmoothIntervalMs, MaxTouchupConstantSmoothIntervalMs);
                if (interval != touchupConstantSmoothIntervalMs)
                {
                    TouchupConstantSmoothIntervalMs = interval;
                }
                EditorGUI.indentLevel--;
            }
        }

        /// <summary>
        /// Called before the brush is applied on a repaint while the button is held. When constant
        /// application is on for the Smooth brush and an interval has passed, the vertices this stroke
        /// already smoothed may be smoothed again.
        /// </summary>
        /// <returns>True when the stroke was opened for another application.</returns>
        private bool PrepareTouchupConstantSmooth()
        {
            LoadTouchupConstantSmoothPrefs();
            if (!touchupWeightsMode || touchupWeightTool != TouchupWeightTool.Paint ||
                touchupWeightPaintMode != TouchupWeightPaintMode.Smooth || !touchupConstantSmooth)
            {
                return false;
            }
            double now = EditorApplication.timeSinceStartup;
            if (!touchupPaintStrokeActive)
            {
                // Not painting yet: the first repeat comes one interval after the stroke starts.
                touchupNextConstantSmooth = now + touchupConstantSmoothIntervalMs / 1000.0;
                touchupConstantSmoothRepeats = 0;
                return false;
            }
            if (now < touchupNextConstantSmooth)
            {
                return false;
            }
            // One application per repaint at most: a slow frame does not cause a burst afterwards.
            touchupNextConstantSmooth = now + touchupConstantSmoothIntervalMs / 1000.0;
            touchupConstantSmoothRepeats++;
            selectionBrushedVerticesThisStroke.RemoveWhere(IsTouchupWeightStrokeKey);
            return true;
        }

        private static bool IsTouchupWeightStrokeKey(string key)
        {
            return key != null && key.StartsWith(TouchupWeightStrokeKeyPrefix, StringComparison.Ordinal);
        }
    }
}
