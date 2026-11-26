using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The garment generator's panel, after Divine Cut's: pick a garment type, start from a preset, move the style
    /// sliders, and the Clothes Designer drafts the pattern to the body in the open Vertex Editor and drapes it. With
    /// Live on, every change is drafted again as soon as the slider is let go. The pattern is an ordinary one: edit it
    /// on the Pattern Canvas, fit it, bake it.
    /// </summary>
    public sealed class GarmentGeneratorWindow : EditorWindow
    {
        private const string Title = "Garment Generator";
        /// <summary>How long after the last change Live drafts again (seconds), and how long while a slider is held.</summary>
        private const double LiveDelay = 0.35, LiveDelayHeld = 1.0;

        [SerializeField] private string styleJson;
        [SerializeField] private bool live = true;
        private GarmentStyle style;
        private DesignerSession session;
        /// <summary>The pattern this window last put in the session, and how it was then: changed since, it has the artist's edits.</summary>
        private ClothesPattern generated;
        private string generatedJson;
        private GarmentBody body;
        private BodyReference bodyFrom;
        private double changedAt = -1;
        private bool pending;
        private string status;
        private Vector2 scroll;
        private GUIStyle tileLabel, header, statusStyle;

        private static readonly GarmentType[] Types = { GarmentType.Top, GarmentType.Dress, GarmentType.Kimono, GarmentType.Trousers, GarmentType.Skirt, GarmentType.Jumpsuit };
        private static readonly string[] TypeNames = { "Top", "Dress", "Kimono", "Trousers", "Skirt", "Jumpsuit" };
        private static readonly string[] NecklineNames = { "Crew", "Scoop", "V", "Square", "Boat" };
        private static readonly string[] CollarNames = { "None", "Band", "Shirt", "Hood", "Turtleneck" };
        private static readonly string[] FrontNames = { "Closed", "Open", "Wrap" };
        private static readonly string[] BandNames = { "None", "Same Fabric", "Rib" };

        [MenuItem("UMA/Clothes Designer/Garment Generator", priority = 131)]
        private static void OpenMenu() => Open(FindSession());

        /// <summary>Opens the generator on a designer session (null finds the open one, if any).</summary>
        public static GarmentGeneratorWindow Open(DesignerSession session)
        {
            var window = GetWindow<GarmentGeneratorWindow>(Title);
            window.minSize = new Vector2(380f, 520f);
            window.Bind(session ?? FindSession());
            window.Show();
            return window;
        }

        /// <summary>The designer session in the open Vertex Editor, or null.</summary>
        public static DesignerSession FindSession()
        {
            var stage = StageUtility.GetCurrentStage() as VertexEditorStage;
            return stage != null && ClothesDesignerWorkflow.Instance.TryGetSession(stage, out DesignerSession s) && !s.IsDisposed ? s : null;
        }

        private void Bind(DesignerSession next)
        {
            if (next == session) return;
            if (session != null) session.Changed -= Repaint;
            session = next;
            if (session != null)
            {
                session.Changed += Repaint;
                // A pattern the generator drafted (this time or saved before) is opened at its style, and is its own.
                GarmentStyle saved = session.Pattern != null ? GarmentStyle.FromJson(session.Pattern.generatorStyle) : null;
                if (saved != null)
                {
                    style = saved;
                    SaveStyle();
                    generated = session.Pattern;
                    generatedJson = JsonUtility.ToJson(generated);
                }
            }
        }

        private void OnEnable()
        {
            style = GarmentStyle.FromJson(styleJson) ?? GarmentPresets.Default(GarmentType.Top);
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            if (session != null) session.Changed -= Repaint;
        }

        private void SaveStyle() => styleJson = style.ToJson();

        // ---- Drafting -------------------------------------------------------------------------------

        private GarmentBody BodyFor(DesignerSession s)
        {
            if (s.Body != bodyFrom || body == null)
            {
                bodyFrom = s.Body;
                body = GarmentBody.From(s.Body);
            }
            return body;
        }

        /// <summary>True while the session's pattern is the one this window drafted, unchanged since.</summary>
        private bool PatternIsOurs => session != null && session.Pattern != null && session.Pattern == generated && JsonUtility.ToJson(session.Pattern) == generatedJson;

        /// <summary>Drafts the style on the session's body and drapes it.</summary>
        /// <param name="asked">The artist pressed Generate: a pattern that is not the generator's may be replaced, after asking.</param>
        private void Generate(bool asked)
        {
            pending = false;
            if (session == null || session.IsDisposed) { status = "Open the Clothes Designer first."; return; }
            if (session.Body == null) { status = "The body could not be read: " + session.BodyProblem; return; }
            bool ours = PatternIsOurs;
            if (!ours && session.HasPattern)
            {
                if (!asked) { status = "The pattern has been edited since it was drafted. Press Generate to draft it again (your edits will be replaced)."; return; }
                if (session.HasUnsavedChanges)
                {
                    int choice = EditorUtility.DisplayDialogComplex(Title, session.DescribeUnsavedChanges() + "\n\nGenerating replaces the pattern being edited.", "Save", "Cancel", "Discard");
                    if (choice == 1) return;
                    if (choice == 0 && !session.SaveAll()) return;
                    if (choice == 2) session.DiscardAll();
                }
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            ClothesPattern pattern = GarmentGenerator.Generate(style, BodyFor(session), false);
            long drafted = clock.ElapsedMilliseconds;
            // The redraft of the generator's own pattern keeps the asset it was saved to; anything else starts a new pattern.
            if (ours && session.PatternAsset != null) session.ReplacePattern(pattern);
            else session.NewPattern(pattern);
            generated = session.Pattern;
            generatedJson = JsonUtility.ToJson(generated);
            // Generate frames the new garment; a live redraft leaves the view where the artist put it.
            if (asked) session.FrameCloth();
            if (session.ClothReady) session.SetRunning(true);
            clock.Stop();
            status = "Drafted in " + drafted + " ms (laid out in " + (clock.ElapsedMilliseconds - drafted) + " ms): " + pattern.pieces.Count + " pieces, "
                + pattern.seams.Count + " seams, " + (session.ClothReady ? session.Fit.Mesh.VertexCount.ToString("N0") + " vertices." : "no cloth: " + string.Join(" ", session.Fit.Messages));
            SceneView.RepaintAll();
        }

        private void Tick()
        {
            if (!pending || !live) return;
            double since = EditorApplication.timeSinceStartup - changedAt;
            bool held = GUIUtility.hotControl != 0;
            if (since < (held ? LiveDelayHeld : LiveDelay)) return;
            Generate(false);
            Repaint();
        }

        private void Changed()
        {
            if (style.preset != GarmentPresets.Custom) style.basedOn = style.preset;
            style.preset = GarmentPresets.Custom;
            Touched();
        }

        private void Touched()
        {
            style.Clamp();
            SaveStyle();
            changedAt = EditorApplication.timeSinceStartup;
            pending = live && PatternIsOurs;
        }

        /// <summary>Packs the UVs tightly (the live drafts lay them out quickly) and keeps the drape.</summary>
        private void Finish()
        {
            if (session == null || session.Pattern == null) return;
            UvLayout.Repack(session.Pattern, out float coverage);
            if (session.ClothReady) UvLayout.ApplyUVs(session.Pattern, session.Fit.Mesh);
            if (session.Pattern == generated) generatedJson = JsonUtility.ToJson(generated);
            session.RefreshView();
            status = "UVs packed tightly: the cloth covers " + (coverage * 100f).ToString("0") + "% of the texture. Bake it from the Clothes Designer panel, or save the pattern.";
        }

        // ---- GUI -----------------------------------------------------------------------------------

        private void EnsureStyles()
        {
            if (tileLabel != null) return;
            tileLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.LowerCenter };
            header = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            statusStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (session == null || session.IsDisposed) Bind(FindSession());
            if (style == null) style = GarmentPresets.Default(GarmentType.Top);

            EditorGUILayout.Space(4f);
            GUILayout.Label(Title, header);
            if (session == null)
            {
                EditorGUILayout.HelpBox("The generator drafts clothes to a body. Open the Clothes Designer on a race, then come back here.", MessageType.Info);
                if (GUILayout.Button("Create Clothes on Race...", GUILayout.Height(26f))) EditorApplication.ExecuteMenuItem("UMA/Clothes Designer/Create Clothes on Race...");
                return;
            }
            if (session.Body != null)
            {
                GarmentBody b = BodyFor(session);
                RaceData race = session.Stage != null ? session.Stage.CharacterRace : null;
                GUILayout.Label((race != null ? race.raceName + ": " : "") + "chest " + b.chest.ToString("0") + ", waist " + b.waist.ToString("0") + ", hip " + b.hip.ToString("0") + " cm. Every size is drafted to this body.", EditorStyles.wordWrappedMiniLabel);
            }

            DrawTypeTiles();
            DrawPresetRow();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            DrawSections();
            if (EditorGUI.EndChangeCheck()) Changed();
            EditorGUILayout.EndScrollView();
            DrawFooter();
        }

        private void DrawTypeTiles()
        {
            const float size = 58f;
            Rect row = GUILayoutUtility.GetRect(position.width - 8f, size + 4f);
            float width = Mathf.Min(size + 6f, (row.width - 4f) / Types.Length);
            for (int i = 0; i < Types.Length; i++)
            {
                var r = new Rect(row.x + 2f + i * width, row.y + 2f, width - 4f, size);
                bool on = style.type == Types[i];
                if (GUI.Toggle(r, on, GUIContent.none, "Button") && !on)
                {
                    GarmentStyle keep = style;
                    style = GarmentPresets.Default(Types[i]);
                    // The fabric the artist chose carries over where the new type has the same kind of cloth.
                    if (keep.HasTop == style.HasTop && keep.HasLegs == style.HasLegs) style.fabric = keep.fabric;
                    Touched();
                    GUI.changed = false;
                }
                if (Event.current.type == EventType.Repaint) DrawIcon(new Rect(r.x + 8f, r.y + 4f, r.width - 16f, r.height - 20f), Types[i], on);
                GUI.Label(new Rect(r.x, r.y, r.width, r.height - 2f), TypeNames[i], tileLabel);
            }
        }

        private void DrawPresetRow()
        {
            List<GarmentPreset> presets = GarmentPresets.ForType(style.type);
            var names = new List<string>();
            int index = -1;
            for (int i = 0; i < presets.Count; i++)
            {
                names.Add(presets[i].Name);
                if (presets[i].Name == style.preset) index = i;
            }
            if (index < 0) { names.Add(GarmentPresets.Custom); index = names.Count - 1; }
            EditorGUILayout.BeginHorizontal();
            int picked = EditorGUILayout.Popup(new GUIContent("Preset", "A full style to start from. Moving any slider makes it Custom."), index, names.ToArray());
            if (picked != index && picked < presets.Count)
            {
                style = presets[picked].Make();
                Touched();
            }
            if (GUILayout.Button(new GUIContent("Reset", "Back to the preset's values (the type's first preset for a custom style)."), GUILayout.Width(52f)))
            {
                GarmentPreset p = GarmentPresets.Find(style.preset);
                style = p != null && p.Type == style.type ? p.Make() : GarmentPresets.Default(style.type);
                Touched();
            }
            EditorGUILayout.EndHorizontal();
            GarmentPreset current = GarmentPresets.Find(style.preset);
            if (current != null) GUILayout.Label(current.Description, EditorStyles.wordWrappedMiniLabel);
        }

        private static bool Section(string key, string title)
        {
            string pref = "UMA.ClothesDesigner.Generator." + key;
            bool open = SessionState.GetBool(pref, true);
            bool now = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            if (now != open) SessionState.SetBool(pref, now);
            return now;
        }

        private float Stop(string label, string tip, float value, string[] stops, Func<float, float> heightOf)
        {
            float v = EditorGUILayout.Slider(new GUIContent(label, tip), value, 0f, stops.Length - 1);
            EditorGUILayout.LabelField(" ", GarmentGenerator.StopName(stops, v) + " (" + heightOf(v).ToString("0") + " cm below the shoulders)", EditorStyles.miniLabel);
            return v;
        }

        private void DrawSections()
        {
            GarmentBody b = session != null && session.Body != null ? BodyFor(session) : GarmentBody.Default;
            GarmentStyle s = style;
            bool top = s.HasTop, legs = s.HasLegs, skirt = s.type == GarmentType.Skirt;

            if (Section("Body", top ? "Length and Fit" : skirt ? "Skirt" : "Fit"))
            {
                EditorGUI.indentLevel++;
                if (top && s.type != GarmentType.Jumpsuit || skirt)
                    s.length = Stop("Length", "Where the hem falls, from a crop top to the floor.", s.length, GarmentGenerator.LengthStops, v => GarmentGenerator.LengthY(b, v));
                string easeLabel = s.type == GarmentType.Jumpsuit ? "Ease (Chest and Hips)" : top ? "Ease (Chest)" : "Ease (Hips)";
                s.easeCm = EditorGUILayout.Slider(new GUIContent(easeLabel + " cm", "Room round the body. 4 fitted, 10 regular, 20 loose, 30 oversized. Below zero the cloth is cut smaller than the body: for stretch fabrics worn tight."), s.easeCm, -20f, 60f);
                if (top && s.type != GarmentType.Jumpsuit)
                {
                    s.waistShaping = EditorGUILayout.Slider(new GUIContent("Waist Shaping", "How far the sides follow the waist in: 0 straight, 1 fitted. Fitted woven cloth gets waist darts too."), s.waistShaping, 0f, 1f);
                    s.flare = EditorGUILayout.Slider(new GUIContent("Flare", "How far the hem flares out below the hips: 0 straight, 1 full."), s.flare, 0f, 1f);
                    s.hemDropCm = EditorGUILayout.Slider(new GUIContent("Shirt Tail cm", "How much lower the middle of the hem is than its sides."), s.hemDropCm, 0f, 15f);
                }
                if (top) s.shoulderCm = EditorGUILayout.Slider(new GUIContent("Shoulders cm", "Wider than the body's shoulders (dropped shoulders, for oversized tops) or narrower."), s.shoulderCm, -4f, 10f);
                if (skirt)
                {
                    s.rise = EditorGUILayout.Slider(new GUIContent("Waist Height", "Where the top sits: 0 low on the hips, 1 at the waist."), s.rise, 0f, 1f);
                    s.flare = EditorGUILayout.Slider(new GUIContent("Flare", "0 a pencil skirt (darted at the waist), 0.35 an A-line, 1 a full skirt (cut in four gores)."), s.flare, 0f, 1f);
                    s.pleats = EditorGUILayout.IntSlider(new GUIContent("Pleats", "Pressed knife pleats on each panel, stitched down from the waist. 0 for none."), s.pleats, 0, 12);
                }
                EditorGUI.indentLevel--;
            }

            if (legs && Section("Legs", "Legs"))
            {
                EditorGUI.indentLevel++;
                s.legLength = Stop("Leg Length", "Where the legs end, from hot pants to the floor.", s.legLength, GarmentGenerator.LegStops, v => GarmentGenerator.LegY(b, v));
                if (s.type == GarmentType.Trousers) s.rise = EditorGUILayout.Slider(new GUIContent("Rise", "Where the top sits: 0 low on the hips, 0.35 on the hips (jeans), 1 at the waist."), s.rise, 0f, 1f);
                s.thighEaseCm = EditorGUILayout.Slider(new GUIContent("Thigh Width cm", "Room round each thigh. Below zero for leggings."), s.thighEaseCm, -15f, 50f);
                s.hemWidth = EditorGUILayout.Slider(new GUIContent("Hem Width", "0 close round the ankle (skinny), 1 as wide as the thigh (straight), above 1 flared."), s.hemWidth, 0f, 1.6f);
                if (s.type == GarmentType.Trousers)
                {
                    s.waistband = (BandStyle)EditorGUILayout.Popup(new GUIContent("Waistband", "Same Fabric: a band cut to the body at both edges. Rib: a firm rib band cut short, gathering the top in and gripping (joggers)."), (int)s.waistband, BandNames);
                    if (s.waistband != BandStyle.None) s.waistbandCm = EditorGUILayout.Slider(new GUIContent("  Height cm"), s.waistbandCm, 2f, 12f);
                }
                s.legCuffs = (BandStyle)EditorGUILayout.Popup(new GUIContent("Ankle Cuffs", "Bands at the leg hems. Rib gathers them in (joggers)."), (int)s.legCuffs, BandNames);
                EditorGUI.indentLevel--;
            }
            if (skirt && Section("SkirtBand", "Waistband"))
            {
                EditorGUI.indentLevel++;
                s.waistband = (BandStyle)EditorGUILayout.Popup(new GUIContent("Waistband"), (int)s.waistband, BandNames);
                if (s.waistband != BandStyle.None) s.waistbandCm = EditorGUILayout.Slider(new GUIContent("  Height cm"), s.waistbandCm, 2f, 12f);
                EditorGUI.indentLevel--;
            }

            if (top && Section("Neck", "Neck and Collar"))
            {
                EditorGUI.indentLevel++;
                s.neckline = (NecklineShape)GUILayout.Toolbar((int)s.neckline, NecklineNames);
                s.neckWidth = EditorGUILayout.Slider(new GUIContent("Neck Width", "0 close to the neck, 1 out towards the shoulders."), s.neckWidth, 0f, 1f);
                s.neckDepthCm = EditorGUILayout.Slider(new GUIContent("Front Depth cm", "How deep the neckline is at the front, below the neck points."), s.neckDepthCm, 1f, 35f);
                s.backNeckDepthCm = EditorGUILayout.Slider(new GUIContent("Back Depth cm"), s.backNeckDepthCm, 1f, 30f);
                s.collar = (CollarStyle)EditorGUILayout.Popup(new GUIContent("Collar", "Band: a straight stand. Shirt: a firm stand with a leaf turned down over it. Hood: two panels sized to the head. Turtleneck: a tube up the neck."), (int)s.collar, CollarNames);
                if (s.collar != CollarStyle.None && s.collar != CollarStyle.Hood) s.collarHeightCm = EditorGUILayout.Slider(new GUIContent("  Height cm"), s.collarHeightCm, 2f, 20f);
                EditorGUI.indentLevel--;
            }

            if (top && Section("Sleeves", "Sleeves"))
            {
                EditorGUI.indentLevel++;
                s.sleeve = EditorGUILayout.Slider(new GUIContent("Length", "A share of the arm, shoulder to wrist: 0 sleeveless, 0.3 short, 0.5 elbow, 1 long."), s.sleeve, 0f, 1.1f);
                EditorGUILayout.LabelField(" ", GarmentGenerator.SleeveName(s.sleeve), EditorStyles.miniLabel);
                if (s.sleeve < 0.04f) s.strapCm = EditorGUILayout.Slider(new GUIContent("Strap Width cm", "The shoulder strap of a sleeveless top."), s.strapCm, 2f, 14f);
                else
                {
                    s.sleeveEaseCm = EditorGUILayout.Slider(new GUIContent("Width cm", "Room round the upper arm. Wide sleeves deepen the armhole."), s.sleeveEaseCm, 0f, 40f);
                    s.sleeveFlare = EditorGUILayout.Slider(new GUIContent("Opening", "0 close round the arm at the hem, 1 a wide bell."), s.sleeveFlare, 0f, 1f);
                    s.cuffs = (BandStyle)EditorGUILayout.Popup(new GUIContent("Cuffs", "Bands at the sleeve hems. Rib gathers them in."), (int)s.cuffs, BandNames);
                }
                EditorGUI.indentLevel--;
            }

            if (top && Section("Front", "Front and Hem"))
            {
                EditorGUI.indentLevel++;
                s.front = (FrontStyle)GUILayout.Toolbar((int)s.front, FrontNames);
                if (s.front == FrontStyle.Wrap) s.overlapCm = EditorGUILayout.Slider(new GUIContent("Overlap cm", "How far each front crosses past the middle. The fronts are tacked together at the waist."), s.overlapCm, 0f, 25f);
                if (s.type != GarmentType.Jumpsuit) s.hemBand = (BandStyle)EditorGUILayout.Popup(new GUIContent("Hem Band", "A band round the hem. Rib gathers it in (a hoodie's)."), (int)s.hemBand, BandNames);
                EditorGUI.indentLevel--;
            }

            bool banded = s.cuffs != BandStyle.None && top && s.sleeve >= 0.04f || s.hemBand != BandStyle.None && top && s.type != GarmentType.Jumpsuit || s.legCuffs != BandStyle.None && legs;
            if (banded)
            {
                EditorGUI.indentLevel++;
                s.bandHeightCm = EditorGUILayout.Slider(new GUIContent("Band Height cm", "The height of cuffs and hem bands."), s.bandHeightCm, 2f, 15f);
                EditorGUI.indentLevel--;
            }

            if (Section("Fabric", "Fabric"))
            {
                EditorGUI.indentLevel++;
                string[] fabrics = FabricNames();
                int f = Mathf.Max(0, Array.IndexOf(fabrics, s.fabric));
                s.fabric = fabrics[EditorGUILayout.Popup(new GUIContent("Fabric", "The garment's cloth. Each piece can be changed afterwards under Fabric in the Clothes Designer."), f, fabrics)];
                FabricPreset preset = FabricPresets.Find(s.fabric);
                if (preset != null) EditorGUILayout.LabelField(" ", preset.Description, EditorStyles.wordWrappedMiniLabel);
                bool trims = banded && (s.cuffs == BandStyle.Self || s.hemBand == BandStyle.Self || s.legCuffs == BandStyle.Self) || s.collar == CollarStyle.Turtleneck;
                if (trims)
                {
                    int t = Mathf.Max(0, Array.IndexOf(fabrics, s.trimFabric));
                    s.trimFabric = fabrics[EditorGUILayout.Popup(new GUIContent("Trim Fabric", "For a turtleneck (when it stretches) and for cuffs and bands in the same fabric. Rib bands are always Rib."), t, fabrics)];
                }
                EditorGUI.indentLevel--;
            }
        }

        private static string[] FabricNames()
        {
            var names = new string[FabricPresets.All.Count];
            for (int i = 0; i < names.Length; i++) names[i] = FabricPresets.All[i].Name;
            return names;
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(2f);
            Rect line = GUILayoutUtility.GetRect(position.width, 1f);
            EditorGUI.DrawRect(line, new Color(0f, 0f, 0f, 0.25f));
            EditorGUILayout.BeginHorizontal();
            bool newLive = GUILayout.Toggle(live, new GUIContent("Live", "Draft and drape again whenever a value changes (as soon as the slider is let go)."), GUILayout.Width(48f));
            if (newLive != live) { live = newLive; if (live) Touched(); }
            if (GUILayout.Button(new GUIContent(PatternIsOurs ? "Generate Again" : "Generate", "Draft the garment to the body and drape it."), GUILayout.Height(28f))) Generate(true);
            using (new EditorGUI.DisabledScope(session == null || session.Pattern == null))
            {
                if (GUILayout.Button(new GUIContent("Done", "Pack the UVs tightly (live drafts lay them out quickly; the bake does this too). The drape is kept."), GUILayout.Height(28f), GUILayout.Width(56f))) Finish();
                if (GUILayout.Button(new GUIContent("Save...", "Save the pattern and the fit as assets. The pattern keeps its style, so the generator can open it again."), GUILayout.Height(28f), GUILayout.Width(60f))) session.SaveAll();
            }
            EditorGUILayout.EndHorizontal();
            if (session != null && session.ClothReady)
            {
                FitSession fit = session.Fit;
                string cloth = session.Running ? (!fit.Sewn ? "Sewing " + Mathf.RoundToInt((1f - fit.Closure) * 100f) + "%" : "Settling") : (session.Settled ? "Settled" : (fit.SimTime <= 0f ? "Laid out round the body" : "Paused"));
                GUILayout.Label("Cloth: " + cloth + (pending ? "  (drafting again...)" : ""), EditorStyles.miniBoldLabel);
            }
            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status, statusStyle);
        }

        // ---- Icons ---------------------------------------------------------------------------------

        private static readonly Dictionary<GarmentType, Vector2[][]> Icons = new Dictionary<GarmentType, Vector2[][]>
        {
            { GarmentType.Top, new[] { P(0.35f, 0.12f, 0.2f, 0.2f, 0.06f, 0.4f, 0.2f, 0.47f, 0.28f, 0.38f, 0.28f, 0.9f, 0.72f, 0.9f, 0.72f, 0.38f, 0.8f, 0.47f, 0.94f, 0.4f, 0.8f, 0.2f, 0.65f, 0.12f, 0.58f, 0.19f, 0.42f, 0.19f, 0.35f, 0.12f) } },
            { GarmentType.Dress, new[] { P(0.39f, 0.06f, 0.37f, 0.3f, 0.32f, 0.44f, 0.16f, 0.94f, 0.84f, 0.94f, 0.68f, 0.44f, 0.63f, 0.3f, 0.61f, 0.06f, 0.55f, 0.13f, 0.45f, 0.13f, 0.39f, 0.06f) } },
            { GarmentType.Kimono, new[] { P(0.37f, 0.08f, 0.04f, 0.2f, 0.04f, 0.52f, 0.29f, 0.52f, 0.29f, 0.94f, 0.71f, 0.94f, 0.71f, 0.52f, 0.96f, 0.52f, 0.96f, 0.2f, 0.63f, 0.08f, 0.47f, 0.42f, 0.37f, 0.08f), P(0.47f, 0.42f, 0.6f, 0.94f) } },
            { GarmentType.Trousers, new[] { P(0.26f, 0.06f, 0.74f, 0.06f, 0.82f, 0.94f, 0.57f, 0.94f, 0.5f, 0.34f, 0.43f, 0.94f, 0.18f, 0.94f, 0.26f, 0.06f) } },
            { GarmentType.Skirt, new[] { P(0.33f, 0.16f, 0.67f, 0.16f, 0.88f, 0.84f, 0.12f, 0.84f, 0.33f, 0.16f), P(0.33f, 0.24f, 0.67f, 0.24f) } },
            { GarmentType.Jumpsuit, new[] { P(0.38f, 0.04f, 0.3f, 0.1f, 0.18f, 0.28f, 0.29f, 0.32f, 0.33f, 0.24f, 0.32f, 0.5f, 0.25f, 0.96f, 0.45f, 0.96f, 0.5f, 0.6f, 0.55f, 0.96f, 0.75f, 0.96f, 0.68f, 0.5f, 0.67f, 0.24f, 0.71f, 0.32f, 0.82f, 0.28f, 0.7f, 0.1f, 0.62f, 0.04f, 0.5f, 0.12f, 0.38f, 0.04f) } },
        };

        private static Vector2[] P(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return points;
        }

        private static void DrawIcon(Rect r, GarmentType type, bool on)
        {
            if (!Icons.TryGetValue(type, out Vector2[][] lines)) return;
            float side = Mathf.Min(r.width, r.height);
            var box = new Rect(r.center.x - side * 0.5f, r.center.y - side * 0.5f, side, side);
            Color old = Handles.color;
            Handles.color = on ? new Color(0.3f, 0.6f, 1f) : (EditorGUIUtility.isProSkin ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.2f, 0.2f, 0.2f));
            foreach (Vector2[] line in lines)
            {
                var points = new Vector3[line.Length];
                for (int i = 0; i < line.Length; i++) points[i] = new Vector3(box.x + line[i].x * box.width, box.y + line[i].y * box.height, 0f);
                Handles.DrawAAPolyLine(2f, points);
            }
            Handles.color = old;
        }
    }
}
