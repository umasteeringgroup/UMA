using System;
using System.Collections.Generic;
using System.Globalization;
using UMA.Editors.Weighting;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Touchup Weights: Copy Weights from the Body, in a window of its own. The method and its settings are at the
    /// top, the slots to copy from fill the rest (the part that grows when the window does), and the copy button and
    /// the last copy's report are at the bottom. Its size, place, settings and unticked slots are kept between uses.
    /// </summary>
    internal sealed class UmaTouchupCopyWeightsWindow : EditorWindow
    {
        private const string PrefsPrefix = "UMA.TouchupWeights.Copy.";
        private const string WindowTitle = "Copy Weights from the Body";
        private static readonly UMAWeightMethod[] Methods =
        {
            UMAWeightMethod.RobustInpaint,
            UMAWeightMethod.TightSurface,
            UMAWeightMethod.Volumetric,
            UMAWeightMethod.ExactMatch,
            UMAWeightMethod.Auto
        };

        private VertexEditorStage stage;
        private readonly HashSet<string> excludedSlots = new HashSet<string>();
        private Vector2 donorScroll;
        private Vector2 reportScroll;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;
        /// <summary>A new utility window is placed by Unity as it first shows: the saved place is put back on its first draw.</summary>
        [NonSerialized] private bool pendingPlacement;
        /// <summary>
        /// How tall the parts above and below the slot list were when last drawn. The list gets the rest of the window
        /// (IMGUI's own stretching kept it at its minimum height in this window, measured 2026-11-25).
        /// </summary>
        [NonSerialized] private float aboveList = 300f, belowList = 60f;

        internal static UmaTouchupCopyWeightsWindow Open(VertexEditorStage owner)
        {
            bool existed = HasOpenInstances<UmaTouchupCopyWeightsWindow>();
            var window = GetWindow<UmaTouchupCopyWeightsWindow>(true, WindowTitle, true);
            window.titleContent = new GUIContent(WindowTitle, EditorGUIUtility.IconContent("SkinnedMeshRenderer Icon").image);
            window.minSize = new Vector2(380f, 440f);
            window.stage = owner;
            window.LoadExcludedSlots();
            if (!existed) window.pendingPlacement = true;
            window.Focus();
            window.Repaint();
            return window;
        }

        /// <summary>Closes the window if it is open (Touchup Weights closed).</summary>
        internal static void CloseIfOpen()
        {
            foreach (UmaTouchupCopyWeightsWindow window in Resources.FindObjectsOfTypeAll<UmaTouchupCopyWeightsWindow>())
            {
                if (window != null) window.Close();
            }
        }

        private void OnDisable()
        {
            UmaEditorWindowPlacement.Save(this, PrefsPrefix + "Window");
            SaveExcludedSlots();
        }

        private void OnInspectorUpdate()
        {
            // The stage went (closed, or scripts reloaded): this window has nothing left to work on.
            if (stage == null || !stage.IsTouchupWeightsMode)
            {
                Close();
                return;
            }
            Repaint();
        }

        private void LoadExcludedSlots()
        {
            excludedSlots.Clear();
            string saved = EditorPrefs.GetString(PrefsPrefix + "ExcludedSlots", string.Empty);
            foreach (string name in saved.Split('\n'))
            {
                if (!string.IsNullOrEmpty(name)) excludedSlots.Add(name);
            }
        }

        private void SaveExcludedSlots()
        {
            var names = new List<string>(excludedSlots);
            names.Sort(StringComparer.Ordinal);
            EditorPrefs.SetString(PrefsPrefix + "ExcludedSlots", string.Join("\n", names));
        }

        private void OnGUI()
        {
            if (pendingPlacement && Event.current.type == EventType.Repaint)
            {
                pendingPlacement = false;
                UmaEditorWindowPlacement.Restore(this, PrefsPrefix + "Window", new Vector2(460f, 680f));
            }
            if (stage == null || !stage.IsTouchupWeightsMode)
            {
                EditorGUILayout.HelpBox("Touchup Weights is no longer open.", MessageType.Warning);
                if (GUILayout.Button("Close")) Close();
                return;
            }

            SlotData slot = stage.TouchupWeightSlot;
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Slot", slot != null ? slot.slotName : "None", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("New weights for this slot, copied from the character's other slots. They are staged like painted weights: Undo, Revert and Save in Touchup Weights work as usual.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(2f);
            UMAWeightMethod method = DrawMethod();
            bool selectedOnly = DrawSelectedOnly();
            UMAWeightSettings settings = DrawSettings(method);
            List<SlotData> donors = stage.GetTouchupDonorSlots();
            DrawDonorHeader(donors);
            EditorGUILayout.EndVertical();
            Measure(ref aboveList, true);

            DrawDonorSlots(donors);

            EditorGUILayout.BeginVertical();
            using (new EditorGUI.DisabledScope(donors.Count == 0 || slot == null || DonorNames(donors).Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Copy Weights", "Stage new weights for " + (selectedOnly ? "the selected vertices" : "the whole slot") + ". Save Weights in Touchup Weights writes them to the slot."), GUILayout.Height(28f)))
                {
                    Copy(method, selectedOnly, settings, donors);
                    GUIUtility.ExitGUI();
                }
            }
            DrawReport();
            if (!string.IsNullOrEmpty(statusMessage)) EditorGUILayout.HelpBox(statusMessage, statusType);
            EditorGUILayout.EndVertical();
            Measure(ref belowList, false);
        }

        /// <summary>After a repaint, keeps how far down (or how tall) the group just drawn was, and draws again if it changed.</summary>
        private void Measure(ref float value, bool bottomEdge)
        {
            if (Event.current.type != EventType.Repaint) return;
            Rect r = GUILayoutUtility.GetLastRect();
            float measured = bottomEdge ? r.yMax : r.height;
            if (Mathf.Abs(measured - value) < 0.5f) return;
            value = measured;
            Repaint();
        }

        private UMAWeightMethod DrawMethod()
        {
            int index = Mathf.Clamp(EditorPrefs.GetInt(PrefsPrefix + "Method", 0), 0, Methods.Length - 1);
            var labels = new GUIContent[Methods.Length];
            for (int i = 0; i < Methods.Length; i++) labels[i] = new GUIContent(UMAWeightSettings.DisplayName(Methods[i]), UMAWeightSettings.Describe(Methods[i]));
            int chosen = EditorGUILayout.Popup(new GUIContent("Method", "How the weights are copied. Simulated Fit needs the cloth simulation and is in the Clothes Designer."), index, labels);
            if (chosen != index) EditorPrefs.SetInt(PrefsPrefix + "Method", chosen);
            UMAWeightMethod method = Methods[chosen];
            EditorGUILayout.LabelField(UMAWeightSettings.Describe(method), EditorStyles.wordWrappedMiniLabel);
            return method;
        }

        private bool DrawSelectedOnly()
        {
            bool hasSelection = stage.TouchupSelectionCount > 0;
            bool saved = EditorPrefs.GetBool(PrefsPrefix + "SelectedOnly", false);
            using (new EditorGUI.DisabledScope(!hasSelection))
            {
                string label = hasSelection ? "Selected Vertices Only (" + stage.TouchupSelectionCount + " selected)" : "Selected Vertices Only (none selected)";
                bool value = EditorGUILayout.ToggleLeft(new GUIContent(label, "Only the selected vertices get new weights. The rest keep theirs, and the copied weights are filled in to meet them."), saved && hasSelection);
                if (hasSelection && value != saved) EditorPrefs.SetBool(PrefsPrefix + "SelectedOnly", value);
                return value && hasSelection;
            }
        }

        private UMAWeightSettings DrawSettings(UMAWeightMethod method)
        {
            float matchCm = EditorPrefs.GetFloat(PrefsPrefix + "MatchCm", 4f);
            float angle = EditorPrefs.GetFloat(PrefsPrefix + "Angle", 35f);
            int maxBones = EditorPrefs.GetInt(PrefsPrefix + "MaxBones", 4);
            bool mirror = EditorPrefs.GetBool(PrefsPrefix + "Mirror", false);
            EditorGUI.BeginChangeCheck();
            if (method == UMAWeightMethod.RobustInpaint || method == UMAWeightMethod.Volumetric || method == UMAWeightMethod.Auto)
            {
                matchCm = EditorGUILayout.Slider(new GUIContent("Match Distance (cm)", "Cloth this close to the body (and facing the same way) copies it; cloth further out is filled in or takes the field's weights."), matchCm, 0.5f, 15f);
                angle = EditorGUILayout.Slider(new GUIContent("Match Angle", "How far the cloth's normal may turn from the body's and still copy it (degrees)."), angle, 5f, 90f);
            }
            maxBones = EditorGUILayout.IntSlider(new GUIContent("Max Bones", "The most bones a vertex keeps."), maxBones, 1, 8);
            mirror = EditorGUILayout.Toggle(new GUIContent("Mirror Left/Right", "Make the two sides match: each vertex averages with its mirror image, Left and Right bones swapped by name."), mirror);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetFloat(PrefsPrefix + "MatchCm", matchCm);
                EditorPrefs.SetFloat(PrefsPrefix + "Angle", angle);
                EditorPrefs.SetInt(PrefsPrefix + "MaxBones", maxBones);
                EditorPrefs.SetBool(PrefsPrefix + "Mirror", mirror);
            }
            return new UMAWeightSettings
            {
                Method = method,
                MatchDistance = matchCm * 0.01f,
                MatchAngleDegrees = angle,
                MaxBones = maxBones,
                MirrorLeftRight = mirror
            };
        }

        private void DrawDonorHeader(List<SlotData> donors)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            int ticked = DonorNames(donors).Count;
            EditorGUILayout.LabelField(new GUIContent("Copy From (" + ticked + " of " + donors.Count + ")", "The slots whose weights are copied. Untick other clothes to copy from the body alone. Unticked slots stay unticked next time."), EditorStyles.boldLabel);
            if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(44f)))
            {
                foreach (SlotData donor in donors) excludedSlots.Remove(donor.slotName);
                SaveExcludedSlots();
            }
            if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(44f)))
            {
                foreach (SlotData donor in donors) excludedSlots.Add(donor.slotName);
                SaveExcludedSlots();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>The slots to copy from: the part of the window that takes the room when it grows.</summary>
        private void DrawDonorSlots(List<SlotData> donors)
        {
            // Everything the parts above and below do not use, and never less than a few rows.
            float height = Mathf.Max(90f, position.height - aboveList - belowList - 10f);
            donorScroll = EditorGUILayout.BeginScrollView(donorScroll, EditorStyles.helpBox, GUILayout.Height(height));
            if (donors.Count == 0) EditorGUILayout.LabelField("The character has no other slots to copy from.", EditorStyles.wordWrappedMiniLabel);
            foreach (SlotData donor in donors)
            {
                bool on = !excludedSlots.Contains(donor.slotName);
                string assetName = donor.asset != null ? donor.asset.name : string.Empty;
                bool now = EditorGUILayout.ToggleLeft(new GUIContent(donor.slotName, assetName), on);
                if (now == on) continue;
                if (now) excludedSlots.Remove(donor.slotName); else excludedSlots.Add(donor.slotName);
                SaveExcludedSlots();
            }
            EditorGUILayout.EndScrollView();
        }

        private HashSet<string> DonorNames(List<SlotData> donors)
        {
            var names = new HashSet<string>();
            foreach (SlotData donor in donors) if (!excludedSlots.Contains(donor.slotName)) names.Add(donor.slotName);
            return names;
        }

        private void Copy(UMAWeightMethod method, bool selectedOnly, UMAWeightSettings settings, List<SlotData> donors)
        {
            var options = new VertexEditorStage.TouchupCopyOptions
            {
                SelectedOnly = selectedOnly,
                DonorSlots = DonorNames(donors),
                Settings = settings
            };
            SlotData slot = stage.TouchupWeightSlot;
            EditorUtility.DisplayProgressBar(WindowTitle, "Weighting " + (slot != null ? slot.slotName : "the slot") + " (" + UMAWeightSettings.DisplayName(method) + ")...", 0.5f);
            try
            {
                bool ok = stage.TryCopyTouchupWeightsFromBody(options, out statusMessage);
                statusType = ok ? MessageType.Info : MessageType.Error;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            reportScroll = Vector2.zero;
        }

        private void DrawReport()
        {
            UMAWeightReport report = stage.LastTouchupCopyReport;
            if (report == null || (report.Scores.Count == 0 && report.Messages.Count == 0)) return;
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MaxHeight(120f));
            foreach (UMAWeightScore score in report.Scores)
            {
                string mark = score.Method == report.Used ? "> " : "   ";
                EditorGUILayout.LabelField(mark + score, EditorStyles.wordWrappedMiniLabel);
            }
            foreach (string message in report.Messages) EditorGUILayout.LabelField(message, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// Keeps a floating editor window's size and place between openings (EditorPrefs). A docked window keeps its
    /// place in the layout and is left alone, so a saved place never moves or resizes the user's layout.
    /// </summary>
    internal static class UmaEditorWindowPlacement
    {
        internal static void Save(EditorWindow window, string key)
        {
            if (window == null || window.docked) return;
            Rect r = window.position;
            if (r.width < 50f || r.height < 50f) return;
            EditorPrefs.SetString(key, string.Join(",", new[] { r.x, r.y, r.width, r.height }.ToStrings()));
        }

        /// <summary>
        /// Puts a window that is not docked where it was last saved, at least its minimum size. A place that is no
        /// longer near the Editor (a monitor gone) gives way to the saved size centred on the Editor.
        /// </summary>
        internal static void Restore(EditorWindow window, string key, Vector2 defaultSize)
        {
            if (window == null || window.docked) return;
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            Rect saved;
            if (!TryParse(EditorPrefs.GetString(key, string.Empty), out saved))
            {
                window.position = Centred(main, Vector2.Max(defaultSize, window.minSize));
                return;
            }
            var size = new Vector2(Mathf.Max(saved.width, window.minSize.x), Mathf.Max(saved.height, window.minSize.y));
            var near = new Rect(main.x - main.width, main.y - main.height, main.width * 3f, main.height * 3f);
            var placed = new Rect(saved.position, size);
            window.position = near.Overlaps(placed) ? placed : Centred(main, size);
        }

        private static Rect Centred(Rect main, Vector2 size) => new Rect(main.center - size * 0.5f, size);

        private static bool TryParse(string text, out Rect rect)
        {
            rect = default;
            string[] parts = text.Split(',');
            if (parts.Length != 4) return false;
            var v = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || float.IsNaN(v[i]) || float.IsInfinity(v[i])) return false;
            }
            if (v[2] < 50f || v[3] < 50f) return false;
            rect = new Rect(v[0], v[1], v[2], v[3]);
            return true;
        }

        private static string[] ToStrings(this float[] values)
        {
            var s = new string[values.Length];
            for (int i = 0; i < values.Length; i++) s[i] = values[i].ToString("R", CultureInfo.InvariantCulture);
            return s;
        }
    }
}
