using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The sample browser: every sample pattern as a tile with two pictures, the pattern and its drape on a body, in
    /// groups (tops, bottoms, dresses and skirts, underwear and swim, accessories, medieval) and with a search field.
    /// Load (or a double-click) opens the selected sample in the designer. Render on This Body drapes every sample on
    /// the open designer's body in the background, a few frames per Editor update, and keeps the pictures for that race.
    /// </summary>
    public sealed class SampleBrowserWindow : EditorWindow
    {
        private const string Title = "Clothes Samples";
        private const string All = "All";
        private const float TileWidth = 224f, TileHeight = 214f, Gap = 8f;
        /// <summary>How long one Editor update may spend drawing pattern pictures or simulating (ms).</summary>
        private const double Budget = 10.0;
        /// <summary>
        /// The most simulation frames queued in one update. The GPU runs them after the update returns; on the dense head
        /// a small cap costs about 10 ms of GPU time a frame, so more would slow the Editor's own drawing.
        /// </summary>
        private const int FramesPerTick = 4;
        /// <summary>The shortest time between two batches of frames (s): at most half the GPU, even on the head.</summary>
        private const double AdvanceInterval = 0.08;
        private double lastAdvance;

        [SerializeField] private string category = All;
        [SerializeField] private string search = "";
        [SerializeField] private string selected;
        private DesignerSession session;
        private Vector2 scroll;
        private List<string> names;
        private readonly Dictionary<string, Info> infos = new Dictionary<string, Info>();
        private readonly Queue<string> renderQueue = new Queue<string>();
        private SampleDrape current;
        private string renderRace;
        private Mesh bodyMesh;
        private BodyReference bodyFrom;
        private int renderTotal, renderDone;
        private string status;
        private GUIStyle nameStyle, infoStyle, noteStyle, placeholderStyle;

        private struct Info
        {
            public string notes;
            public int pieces;
            public string fabrics;
        }

        [MenuItem("UMA/Clothes Designer/Sample Browser", priority = 132)]
        private static void OpenMenu() => Open(GarmentGeneratorWindow.FindSession());

        /// <summary>Opens the browser on a designer session (null finds the open one, if any).</summary>
        public static SampleBrowserWindow Open(DesignerSession session)
        {
            var window = GetWindow<SampleBrowserWindow>(Title);
            window.minSize = new Vector2(TileWidth + 2f * Gap + 16f, 380f);
            window.Bind(session ?? GarmentGeneratorWindow.FindSession());
            window.Show();
            return window;
        }

        /// <summary>The samples in the browser's order: the vest, then the rest as listed.</summary>
        public static List<string> SampleNames()
        {
            var list = new List<string> { "Sleeveless Vest" };
            foreach (var sample in SamplePatterns.All) list.Add(sample.name);
            return list;
        }

        public static string CategoryOf(string name) => name == "Sleeveless Vest" ? "Tops" : SamplePatterns.Category(name);

        private void Bind(DesignerSession next)
        {
            if (next == session) return;
            if (session != null) session.Changed -= Repaint;
            StopRendering(null);
            session = next;
            if (session != null) session.Changed += Repaint;
        }

        private void OnEnable()
        {
            names = SampleNames();
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            StopRendering(null);
            if (session != null) session.Changed -= Repaint;
            if (bodyMesh != null) DestroyImmediate(bodyMesh);
            bodyMesh = null;
        }

        private string RaceName => session != null && session.Stage != null && session.Stage.CharacterRace != null ? session.Stage.CharacterRace.raceName : null;

        // ---- Background work ---------------------------------------------------------------------------

        private void Tick()
        {
            if (current == null && renderQueue.Count == 0) return;
            if (session == null || session.IsDisposed || session.Body == null)
            {
                StopRendering("The designer closed: rendering stopped.");
                return;
            }
            if (current == null)
            {
                string name = renderQueue.Dequeue();
                current = new SampleDrape(name, session.Body, session.Stage != null ? session.Stage.CharacterRace : null);
                Repaint();
                return;
            }
            if (current.Building)
            {
                // Building the cloth (the mesh, its place round the body, the constraints) is done one part per update.
                current.Advance(0);
                Repaint();
                return;
            }
            // A few frames at a time, spaced out, so the GPU has time for the Editor's own drawing in between.
            double now = EditorApplication.timeSinceStartup;
            if (now - lastAdvance < AdvanceInterval && !current.Done) return;
            lastAdvance = now;
            current.Advance(FramesPerTick);
            if (current.Done)
            {
                if (bodyMesh == null || bodyFrom != session.Body)
                {
                    if (bodyMesh != null) DestroyImmediate(bodyMesh);
                    bodyMesh = SamplePictures.BodyMesh(session.Body);
                    bodyFrom = session.Body;
                }
                Texture2D picture = current.Picture(bodyMesh);
                if (picture != null) SamplePictures.KeepDrapePicture(current.Name, renderRace, picture);
                else status = "'" + current.Name + "' could not be draped" + (current.Problem != null ? ": " + current.Problem : ".");
                current.Dispose();
                current = null;
                renderDone++;
                if (renderQueue.Count == 0) status = "Rendered " + renderDone + " drape pictures on " + renderRace + ".";
            }
            Repaint();
        }

        private void StartRendering()
        {
            StopRendering(null);
            renderRace = RaceName ?? "Body";
            foreach (string name in names) renderQueue.Enqueue(name);
            renderTotal = renderQueue.Count;
            renderDone = 0;
            status = null;
        }

        private void StopRendering(string reason)
        {
            renderQueue.Clear();
            current?.Dispose();
            current = null;
            if (reason != null) status = reason;
        }

        /// <summary>Pattern pictures and sample information not yet made, a few per repaint (within the budget).</summary>
        private bool PrepareSome(List<string> shown)
        {
            var clock = Stopwatch.StartNew();
            bool more = false;
            foreach (string name in shown)
            {
                if (infos.ContainsKey(name)) continue;
                if (clock.Elapsed.TotalMilliseconds > Budget * 3) { more = true; break; }
                ClothesPattern pattern = SamplePatterns.CreateQuick(name);
                var info = new Info { notes = pattern != null ? pattern.notes : "", pieces = pattern != null ? pattern.pieces.Count : 0 };
                if (pattern != null)
                {
                    var fabrics = new List<string>();
                    foreach (PatternPiece piece in pattern.pieces)
                    {
                        string f = piece?.fabric != null && !string.IsNullOrEmpty(piece.fabric.preset) ? piece.fabric.preset : "Custom";
                        if (!fabrics.Contains(f)) fabrics.Add(f);
                    }
                    info.fabrics = string.Join(", ", fabrics);
                    DestroyImmediate(pattern);
                }
                infos[name] = info;
                SamplePictures.PatternPicture(name);
            }
            return more;
        }

        // ---- Drawing -----------------------------------------------------------------------------------

        private void Styles()
        {
            if (nameStyle != null) return;
            nameStyle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };
            infoStyle = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
            noteStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            placeholderStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
        }

        private List<string> Shown()
        {
            var shown = new List<string>();
            foreach (string name in names)
            {
                if (category != All && CategoryOf(name) != category) continue;
                if (!MatchesSearch(name, search)) continue;
                shown.Add(name);
            }
            return shown;
        }

        /// <summary>
        /// Whether a sample's name holds the search text (any case). Its group is not searched: the group buttons pick
        /// groups, and a match inside a group's name showed a whole group the text is not in ("ar", in "Underwear and
        /// Swim", showed Boxer Shorts, Bra, Swimsuit and Briefs).
        /// </summary>
        public static bool MatchesSearch(string name, string search) =>
            string.IsNullOrEmpty(search) || (name != null && name.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

        private void OnGUI()
        {
            Styles();
            if (session == null || session.IsDisposed) Bind(GarmentGeneratorWindow.FindSession());

            // Groups and search.
            var groups = new List<string> { All };
            groups.AddRange(SamplePatterns.Categories);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int index = Mathf.Max(0, groups.IndexOf(category));
                int picked = GUILayout.Toolbar(index, groups.ToArray(), EditorStyles.toolbarButton, GUI.ToolbarButtonSize.FitToContents);
                if (picked != index) { category = groups[picked]; scroll = Vector2.zero; }
                GUILayout.FlexibleSpace();
                search = GUILayout.TextField(search ?? "", EditorStyles.toolbarSearchField, GUILayout.Width(Mathf.Clamp(position.width * 0.25f, 90f, 200f)));
            }

            List<string> shown = Shown();
            // Pictures not drawn yet are made a few at a time; the next repaint makes more.
            bool more = Event.current.type == EventType.Layout && PrepareSome(shown);

            // The tiles.
            float viewWidth = position.width - 16f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((viewWidth - Gap) / (TileWidth + Gap)));
            int rows = Mathf.CeilToInt(shown.Count / (float)columns);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            Rect area = GUILayoutUtility.GetRect(viewWidth, rows * (TileHeight + Gap) + Gap);
            string race = RaceName;
            for (int i = 0; i < shown.Count; i++)
            {
                int row = i / columns, column = i % columns;
                var tile = new Rect(area.x + Gap + column * (TileWidth + Gap), area.y + Gap + row * (TileHeight + Gap), TileWidth, TileHeight);
                DrawTile(tile, shown[i], race);
            }
            if (shown.Count == 0) GUI.Label(new Rect(area.x, area.y + 20f, viewWidth, 40f), "No sample matches.", placeholderStyle);
            EditorGUILayout.EndScrollView();

            DrawFooter(race);
            if (more) Repaint();
        }

        private void DrawTile(Rect tile, string name, string race)
        {
            Event e = Event.current;
            bool isSelected = name == selected;
            if (e.type == EventType.Repaint)
            {
                Color back = isSelected ? new Color(0.24f, 0.45f, 0.75f, 0.55f) : new Color(0f, 0f, 0f, EditorGUIUtility.isProSkin ? 0.18f : 0.08f);
                EditorGUI.DrawRect(tile, back);
            }
            infos.TryGetValue(name, out Info info);
            float pictureHeight = TileHeight - 46f;
            float half = (TileWidth - 12f) * 0.5f;
            var patternRect = new Rect(tile.x + 4f, tile.y + 4f, half, pictureHeight);
            var drapeRect = new Rect(patternRect.xMax + 4f, tile.y + 4f, half, pictureHeight);
            Texture2D patternPicture = infos.ContainsKey(name) ? SamplePictures.PatternPicture(name) : null;
            if (patternPicture != null) GUI.DrawTexture(patternRect, patternPicture, ScaleMode.ScaleToFit);
            else GUI.Label(patternRect, "Drawing...", placeholderStyle);
            Texture2D drape = SamplePictures.DrapePicture(name, race, out bool onThisRace);
            bool rendering = current != null && current.Name == name;
            if (drape != null)
            {
                if (e.type == EventType.Repaint) EditorGUI.DrawRect(drapeRect, new Color(0.17f, 0.17f, 0.18f));
                GUI.DrawTexture(drapeRect, drape, ScaleMode.ScaleToFit);
                if (!onThisRace && race != null) GUI.Label(new Rect(drapeRect.x, drapeRect.yMax - 14f, drapeRect.width, 14f), "on " + SamplePictures.ShippedRace, placeholderStyle);
            }
            else GUI.Label(drapeRect, rendering ? "Draping..." : "No picture yet. Render on This Body makes one.", placeholderStyle);
            if (rendering)
            {
                float done = current.Frames / (float)SamplePictures.DrapeFrames;
                EditorGUI.DrawRect(new Rect(drapeRect.x, drapeRect.yMax - 3f, drapeRect.width * done, 3f), new Color(0.35f, 0.7f, 1f));
            }
            var nameRect = new Rect(tile.x + 6f, patternRect.yMax + 4f, TileWidth - 12f, 18f);
            GUI.Label(nameRect, new GUIContent(name, info.notes), nameStyle);
            string detail = infos.ContainsKey(name) ? info.pieces + (info.pieces == 1 ? " piece" : " pieces") + " - " + info.fabrics : CategoryOf(name);
            GUI.Label(new Rect(nameRect.x, nameRect.yMax, nameRect.width, 16f), new GUIContent(detail, info.notes), infoStyle);
            if (e.type == EventType.MouseDown && e.button == 0 && tile.Contains(e.mousePosition))
            {
                selected = name;
                if (e.clickCount == 2) Load(name);
                e.Use();
                Repaint();
            }
        }

        private void DrawFooter(string race)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!string.IsNullOrEmpty(selected) && infos.TryGetValue(selected, out Info info))
                {
                    GUILayout.Label(selected + " (" + CategoryOf(selected) + ")", EditorStyles.boldLabel);
                    GUILayout.Label(info.notes, noteStyle);
                }
                else GUILayout.Label("Pick a sample: the pattern is on the left of each tile, its drape on the right in the same colours.", noteStyle);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(session == null || string.IsNullOrEmpty(selected)))
                    {
                        if (GUILayout.Button(new GUIContent("Load", "Open the selected sample in the designer (double-click a tile does the same)."), GUILayout.Height(24f))) Load(selected);
                    }
                    bool busy = current != null || renderQueue.Count > 0;
                    using (new EditorGUI.DisabledScope(session == null || session.Body == null))
                    {
                        if (!busy && GUILayout.Button(new GUIContent("Render on This Body", "Drape every sample on the open designer's body and keep the pictures for this race (in the project's Library). Runs in the background."), GUILayout.Height(24f))) StartRendering();
                    }
                    if (busy && GUILayout.Button("Stop", GUILayout.Height(24f), GUILayout.Width(60f))) StopRendering("Rendering stopped.");
                }
                if (current != null || renderQueue.Count > 0)
                {
                    string now = current != null ? current.Name + " (" + current.Frames + " of " + SamplePictures.DrapeFrames + " frames)" : "starting";
                    GUILayout.Label("Rendering " + (renderDone + 1) + " of " + renderTotal + " on " + renderRace + ": " + now, EditorStyles.miniLabel);
                }
                else if (!string.IsNullOrEmpty(status)) GUILayout.Label(status, EditorStyles.wordWrappedMiniLabel);
                else if (session == null) GUILayout.Label("Open the Clothes Designer (UMA > Clothes Designer > Create Clothes on Race) to load samples or render them on its body.", EditorStyles.wordWrappedMiniLabel);
                else if (race != null) GUILayout.Label("Pictures for " + race + " where rendered; the others show " + SamplePictures.ShippedRace + ".", EditorStyles.miniLabel);
            }
        }

        private void Load(string name)
        {
            if (session == null || session.IsDisposed) { status = "Open the Clothes Designer first."; return; }
            if (!ClothesDesignerWorkflow.ConfirmLosingWork(session)) return;
            ClothesPattern pattern = SamplePatterns.Create(name);
            if (pattern == null) { status = "No sample named '" + name + "'."; return; }
            session.NewPattern(pattern);
            // The sample's pieces, laid out round the body, fill the Scene view.
            session.FrameCloth();
            status = "Loaded " + name + ".";
        }
    }
}
