using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed class PluginManagerWindow : EditorWindow
    {
        private TexturePaintStageController controller;
        private CancellationTokenSource cancellation;
        private float progress;
        private string running;
        private Vector2 scroll;
        private bool diagnosticsExpanded = true;
        private readonly HashSet<string> previewExpanded = new HashSet<string>();
        private readonly Dictionary<string, TexturePaintPluginPreview> commandPreviews = new Dictionary<string, TexturePaintPluginPreview>();
        private readonly Dictionary<string, TexturePaintSamplePreview> brushPreviews = new Dictionary<string, TexturePaintSamplePreview>();

        public static void Open(TexturePaintStageController controller)
        {
            PluginManagerWindow window = GetWindow<PluginManagerWindow>("Overlay Painter Plugins");
            if (!ReferenceEquals(window.controller, controller)) window.DisposePreviews();
            window.controller = controller; window.minSize = new Vector2(520f, 420f); window.Show();
        }

        private void OnGUI()
        {
            if (controller?.Plugins == null)
            {
                DisposePreviews();
                EditorGUILayout.HelpBox("Open this window from an active Overlay Painter stage.", MessageType.Info); return;
            }
            EditorGUILayout.HelpBox($"Plugin API v{TexturePaintPluginApi.CurrentVersion}. Plugins receive immutable snapshots and submit validated commands; live textures and the TextureStore are never exposed.", MessageType.Info);
            GUILayout.BeginHorizontal();
            TexturePaintStageWindow activeStage = TexturePaintStageWindow.ActiveStage;
            bool canUndo = activeStage != null ? activeStage.CanUndoPluginTransaction : controller.Plugins.CanUndo;
            bool canRedo = activeStage != null ? activeStage.CanRedoPluginTransaction : controller.Plugins.CanRedo;
            using (new EditorGUI.DisabledScope(!canUndo || cancellation != null))
                if (GUILayout.Button("Undo Plugin Transaction"))
                {
                    if (activeStage != null)
                        activeStage.PerformUndoFromExternalWindow();
                    else controller.Plugins.Undo();
                }
            using (new EditorGUI.DisabledScope(!canRedo || cancellation != null))
                if (GUILayout.Button("Redo Plugin Transaction"))
                {
                    if (activeStage != null)
                        activeStage.PerformRedoFromExternalWindow();
                    else controller.Plugins.Redo();
                }
            GUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawCategory("Brushes", controller.Plugins.Brushes);
            DrawCommandPreviews();
            DrawCategory("Bakers", controller.Plugins.Bakers);
            DrawCategory("Importers", controller.Plugins.Importers);
            DrawCategory("Exporters", controller.Plugins.Exporters);
            if (controller.Plugins.Commands.Count > 0)
                EditorGUILayout.HelpBox(
                    "Generators and filters are selected from dedicated Plugin layers in the layer stack.",
                    MessageType.Info);
            DrawDiagnostics();
            EditorGUILayout.EndScrollView();

            if (cancellation != null)
            {
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, running ?? "Running");
                if (GUILayout.Button("Cancel")) cancellation.Cancel();
                Repaint();
            }
            using (new EditorGUI.DisabledScope(cancellation != null))
                if (GUILayout.Button("Refresh Plugins")) { DisposePreviews(); controller.Plugins.Discover(); }
        }

        private void DrawCommandPreviews()
        {
            if (controller.Plugins.Commands.Count == 0) return;
            EditorGUILayout.LabelField("Generators & Filters", EditorStyles.boldLabel);
            var stage = TexturePaintStageWindow.ActiveStage;
            bool ownsStage = stage?.UsesPluginPreviewController(controller) == true;
            TextureSet set = ownsStage ? stage.PluginPreviewSet :
                controller.Textures?.Sets.Count > 0 ? controller.Textures.Sets[0] : null;
            long version = ownsStage ? stage.PluginPreviewVersion : 0;
            foreach (var plugin in controller.Plugins.Commands)
            {
                if (!TexturePaintPluginPreview.Supports(plugin)) continue;
                string id = plugin.Descriptor.id;
                bool expanded = previewExpanded.Contains(id);
                bool next = EditorGUILayout.Foldout(expanded, plugin.Descriptor.displayName, true);
                if (!next)
                {
                    previewExpanded.Remove(id);
                    if (commandPreviews.Remove(id, out var previous)) previous.Dispose();
                    continue;
                }
                previewExpanded.Add(id);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var values = GetParameters(plugin);
                    EditorGUILayout.LabelField(plugin.Descriptor.description, EditorStyles.wordWrappedMiniLabel);
                    if (set != null)
                    {
                        if (!commandPreviews.TryGetValue(id, out var preview))
                            commandPreviews.Add(id, preview = new TexturePaintPluginPreview(Repaint));
                        preview.Request(controller.Textures, set, null, plugin, values, false, version);
                        preview.Draw();
                    }
                    DrawParameters(plugin.Descriptor, values);
                    EditorGUILayout.LabelField("Preview uses the selected target. Add a Plugin layer to apply a generator or filter.", EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawCategory<T>(string title, IReadOnlyList<T> plugins) where T : ITexturePaintExtensionV2
        {
            if (plugins == null || plugins.Count == 0) return;
            GUILayout.Label(title, EditorStyles.boldLabel);
            for (int i = 0; i < plugins.Count; i++) DrawPlugin(plugins[i]);
        }

        private void DrawPlugin(ITexturePaintExtensionV2 plugin)
        {
            TexturePaintPluginDescriptor descriptor = plugin.Descriptor;
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label(descriptor.displayName + "  " + descriptor.pluginVersion, EditorStyles.boldLabel);
            GUILayout.Label(descriptor.description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("ID", descriptor.id);
            EditorGUILayout.LabelField("Capabilities", descriptor.capabilities.ToString());
            EditorGUILayout.LabelField("Write Channels", descriptor.declaredChannels.ToString());
            EditorGUILayout.LabelField("Read Channels", descriptor.ResolvedReadChannels.ToString());
            if (descriptor.channelSnapshotMaximumResolution > 0)
                EditorGUILayout.LabelField("Input Snapshot Max",
                    descriptor.channelSnapshotMaximumResolution + " px");
            if (descriptor.ResolvedMeshMaps != TexturePaintMeshMapMask.None)
                EditorGUILayout.LabelField("Mesh Maps", descriptor.ResolvedMeshMaps.ToString());
            TexturePaintPluginParameterSet values = GetParameters(plugin);
            if (plugin is ITexturePaintBrushV2 brush)
            {
                if (!brushPreviews.TryGetValue(descriptor.id, out var preview))
                    brushPreviews.Add(descriptor.id, preview = new TexturePaintSamplePreview(Repaint));
                var stage = TexturePaintStageWindow.ActiveStage;
                BrushPreset preset = stage?.UsesPluginPreviewController(controller) == true ? stage.PluginPreviewBrush : null;
                var parameters = values.Clone();
                preview.Request(descriptor.id + JsonUtility.ToJson(parameters) + (preset == null ? "" : JsonUtility.ToJson(preset)),
                    () => TexturePaintBrushPreview.Generate(brush, parameters, preset, TexturePaintChannel.Albedo, Color.white),
                    "Isolated sample stroke with the current brush's spacing and coverage.");
                preview.Draw();
            }
            DrawParameters(descriptor, values);
            using (new EditorGUI.DisabledScope(cancellation != null))
            {
                if (plugin is ITexturePaintCommandExtensionV2 command)
                {
                    string runLabel = plugin is ITexturePaintGeneratorV2 ? "Run Generator" : "Run Filter";
                    if (GUILayout.Button(runLabel)) RunCommand(command, values);
                }
                else if (plugin is ITexturePaintBakerV2 baker && GUILayout.Button("Bake Artifact...")) RunBaker(baker, values);
                else if (plugin is ITexturePaintImporterV2 importer && GUILayout.Button("Import Artifact...")) RunImporter(importer, values);
                else if (plugin is ITexturePaintExporterV2 exporter && GUILayout.Button("Export Artifact...")) RunExporter(exporter, values);
                else if (plugin is ITexturePaintBrushV2) EditorGUILayout.HelpBox("Select this brush in the Overlay Painter tool panel.", MessageType.None);
            }
            EditorGUILayout.EndVertical();
        }

        internal static bool IsTextileParameterVisible(TexturePaintPluginDescriptor descriptor, int mode, string id)
        {
            if (descriptor?.id != "com.uma.texturepaint.textile-surface") return true;
            // These controls are promoted above the collapsible parameter sections.
            if (id == "mode" || id == "modeHeader" || id == "jitter") return false;
            if (id == "quiltAtlasSelection" || id == "quiltAtlasCell" || id == "quiltAtlasAmount" ||
                id == "quiltSpriteSet" || id == "outputSpriteChannels") return mode == 0;
            if (mode == 3 && (id == "scale" || id == "aspect" || id == "breakupScale")) return false;
            string section = null;
            foreach (var parameter in descriptor.parameters)
            {
                if (parameter.type == TexturePaintPluginParameterType.Header) section = parameter.id;
                if (parameter.id != id) continue;
                return section switch
                {
                    "quilt" => mode == 0,
                    "embroidery" => mode == 1,
                    "perforation" => mode == 2,
                    "atlasHeader" => mode == 3,
                    "atlasSource" => mode == 0 || mode == 3,
                    _ => true
                };
            }
            return true;
        }

        internal static void DrawParameters(TexturePaintPluginDescriptor descriptor,
            TexturePaintPluginParameterSet values, Func<string, bool> hideParameter = null)
        {
            if (descriptor == null || values == null) return;
            values.EnsureDefaults(descriptor);
            if (descriptor.id == "com.uma.texturepaint.cloth-texture")
            {
                var rotation = descriptor.parameters.Find(p => p.id == "clothRotation");
                if (rotation != null)
                {
                    var angle = values.Get(rotation.id, true);
                    angle.number = EditorGUILayout.Slider(new GUIContent(rotation.displayName, rotation.description),
                        angle.number, rotation.minimum, rotation.maximum);
                }
                var output = values.Get("outputAlbedo", true);
                output.boolean = EditorGUILayout.Popup(new GUIContent("Fabric Color",
                    "Choose whether this layer changes color or only adds surface detail."), output.boolean ? 1 : 0,
                    new[] { "Keep Existing Color", "Generate Fabric Colors" }) == 1;
                EditorGUILayout.LabelField(output.boolean
                    ? "Base and cross-thread colors mix through the weave. Stripes, motifs and fading are applied afterward. Regenerate to apply changes."
                    : "This layer outputs surface detail only. Enable Generate Fabric Colors to use its color settings.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(!output.boolean))
                    if (GUILayout.Button(new GUIContent("Use Base Color for Both Threads",
                        "Set Cross-Thread Amount to zero so both yarn directions follow Base Color. Weave detail remains.")))
                    { UseClothBaseColor(values); GUI.changed = true; }
                var supplied = hideParameter;
                hideParameter = id => supplied?.Invoke(id) == true || id == "outputAlbedo" || id == "clothRotation" ||
                    (id == "patternColor" && values.Boolean("usePatternColor", true)) ||
                    !IsClothMappingParameterVisible(values, id);
            }
            if (descriptor.id == "com.uma.texturepaint.textile-surface")
            {
                var mode = descriptor.parameters.Find(p => p.id == "mode");
                var selectedMode = values.Get("mode", true);
                selectedMode.number = EditorGUILayout.Popup(new GUIContent("Active Mode",
                    "Choose the surface construction. Quilt can fill its panels with tiles from Atlas Fabric."),
                    Mathf.Clamp(values.Integer("mode"), 0, 3), mode.enumOptions);
                EditorGUILayout.LabelField(values.Integer("mode") == 0
                    ? "Atlas Fabric fills each quilt panel with one tile and keeps the stitches and puff relief."
                    : "One surface construction per layer. Quilt includes its own per-panel Atlas Fabric.",
                    EditorStyles.wordWrappedMiniLabel);
                // Position Jitter is shared by Perforation and Atlas Scatter, though legacy
                // descriptors place it under Perforation. Keep its saved parameter ID.
                if (values.Integer("mode") >= 2)
                {
                    var jitter = descriptor.parameters.Find(p => p.id == "jitter");
                    var value = values.Get("jitter", true);
                    value.number = EditorGUILayout.Slider(new GUIContent(jitter.displayName, jitter.description),
                        value.number, jitter.minimum, jitter.maximum);
                }
                var suppliedFilter = hideParameter;
                hideParameter = id => suppliedFilter?.Invoke(id) == true ||
                    (values.Integer("mode") == 0 && values.Get("quiltSpriteSet")?.spriteSet != null &&
                        (id == "atlas" || id == "atlasColumns" || id == "atlasRows")) ||
                    !IsTextileParameterVisible(descriptor, values.Integer("mode"), id);
            }
            if (descriptor.id == "com.uma.texturepaint.textile-surface" && values.Integer("mode") == 3 &&
                values.Get("atlas", true).texture == null)
                EditorGUILayout.HelpBox("Atlas Scatter needs an Atlas Texture; it scatters images from that texture. " +
                    "Expand Atlas Fabric below, assign the texture, and match Atlas Columns / Rows to its grid " +
                    "(1 by 1 for a single image). Then Generate or Regenerate.", MessageType.Warning);
            bool hasEditableParameters = false;
            for (int i = 0; i < descriptor.parameters.Count; i++)
            {
                TexturePaintPluginParameterDefinition definition = descriptor.parameters[i];
                if (definition != null && definition.type != TexturePaintPluginParameterType.Header &&
                    hideParameter?.Invoke(definition.id) != true)
                {
                    hasEditableParameters = true;
                    break;
                }
            }
            if (hasEditableParameters)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Reset to Defaults",
                        "Restore every parameter in this plugin to its authored default."),
                        EditorStyles.miniButton, GUILayout.Width(130f)))
                {
                    values.ResetToDefaults(descriptor);
                    // Layer and mask inspectors edit cloned parameter sets. Explicitly mark the
                    // IMGUI block changed so their existing undo/persistence path commits reset.
                    GUI.changed = true;
                }
                GUILayout.EndHorizontal();
            }

            bool sectionExpanded = true;
            for (int i = 0; i < descriptor.parameters.Count; i++)
            {
                TexturePaintPluginParameterDefinition definition = descriptor.parameters[i];
                if (definition == null) continue;
                if (definition.type == TexturePaintPluginParameterType.Header)
                {
                    // Promoted controls (for example Fill Type) may leave an empty section.
                    // A hidden header still ends the previous section's collapsed state.
                    sectionExpanded = true;
                    if (hideParameter?.Invoke(definition.id) == true) continue;
                    bool hasVisibleChild = false;
                    for (int child = i + 1; child < descriptor.parameters.Count; child++)
                    {
                        TexturePaintPluginParameterDefinition candidate = descriptor.parameters[child];
                        if (candidate == null) continue;
                        if (candidate.type == TexturePaintPluginParameterType.Header) break;
                        if (hideParameter?.Invoke(candidate.id) != true)
                        { hasVisibleChild = true; break; }
                    }
                    if (!hasVisibleChild) continue;
                    string key = "UMA.OverlayPainter.PluginSection." + descriptor.id + "." + definition.id;
                    bool previous = EditorPrefs.GetBool(key, true);
                    bool parametersChanged = GUI.changed;
                    bool next = EditorGUILayout.Foldout(previous,
                        string.IsNullOrEmpty(definition.displayName) ? definition.id : definition.displayName,
                        true, EditorStyles.foldoutHeader);
                    // Expansion is editor view state, not an edit to the cloned plugin payload.
                    GUI.changed = parametersChanged;
                    if (next != previous) EditorPrefs.SetBool(key, next);
                    sectionExpanded = next;
                    if (next && !string.IsNullOrWhiteSpace(definition.description))
                        EditorGUILayout.LabelField(definition.description,
                            EditorStyles.wordWrappedMiniLabel);
                    continue;
                }
                if (hideParameter?.Invoke(definition.id) == true) continue;
                if (!sectionExpanded) continue;
                TexturePaintPluginParameterValue value = values.Get(definition.id, true);
                GUIContent label = new GUIContent(string.IsNullOrEmpty(definition.displayName) ? definition.id : definition.displayName, definition.description);
                switch (definition.type)
                {
                    case TexturePaintPluginParameterType.Float:
                        value.number = EditorGUILayout.Slider(label, value.number, definition.minimum, definition.maximum); break;
                    case TexturePaintPluginParameterType.Integer:
                        if (descriptor.id == "com.uma.texturepaint.textile-surface" && definition.id == "quiltAtlasCell")
                        {
                            using (new EditorGUI.DisabledScope(values.Integer("quiltAtlasSelection", 1) != 0))
                                value.number = EditorGUILayout.IntSlider(label, Mathf.RoundToInt(value.number), 1,
                                    QuiltTileCount(values));
                        }
                        else value.number = EditorGUILayout.IntSlider(label, Mathf.RoundToInt(value.number), Mathf.RoundToInt(definition.minimum), Mathf.RoundToInt(definition.maximum)); break;
                    case TexturePaintPluginParameterType.Boolean:
                        value.boolean = EditorGUILayout.Toggle(label, value.boolean); break;
                    case TexturePaintPluginParameterType.Color:
                        value.color = EditorGUILayout.ColorField(label, value.color); break;
                    case TexturePaintPluginParameterType.Texture:
                        value.texture = (Texture2D)EditorGUILayout.ObjectField(label, value.texture, typeof(Texture2D), false); break;
                    case TexturePaintPluginParameterType.Sprite:
                        var sprite = (Sprite)EditorGUILayout.ObjectField(label, value.sprite, typeof(Sprite), false);
                        if (sprite != value.sprite && descriptor.id == "com.uma.texturepaint.cloth-texture" && definition.id == "patternSprite")
                            SetClothPatternSprite(values, sprite);
                        else value.sprite = sprite;
                        break;
                    case TexturePaintPluginParameterType.SpriteSet:
                        DrawSpriteSet(label, value, values); break;
                    case TexturePaintPluginParameterType.Font:
                        value.font = (Font)EditorGUILayout.ObjectField(label, value.font,
                            typeof(Font), false); break;
                    case TexturePaintPluginParameterType.Enum:
                        value.number = EditorGUILayout.Popup(label, Mathf.Clamp(Mathf.RoundToInt(value.number), 0, Mathf.Max(0, definition.enumOptions.Length - 1)), definition.enumOptions); break;
                    case TexturePaintPluginParameterType.Curve:
                        value.curve = EditorGUILayout.CurveField(label, value.curve ??
                            AnimationCurve.Linear(0f, 0f, 1f, 1f)); break;
                    case TexturePaintPluginParameterType.StripeList:
                        DrawStripeList(label, value); break;
                    case TexturePaintPluginParameterType.MultilineString:
                        EditorGUILayout.LabelField(label);
                        value.text = EditorGUILayout.TextArea(value.text ?? string.Empty,
                            GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 3f)); break;
                    default:
                        value.text = EditorGUILayout.TextField(label, value.text ?? string.Empty); break;
                }
                if (descriptor.id == "com.uma.texturepaint.cloth-texture" && definition.id == "patternOpacity" &&
                    values.Get("patternSprite")?.sprite != null)
                {
                    if (values.Float("patternOpacity") <= 0f)
                    {
                        EditorGUILayout.HelpBox("Pattern Opacity is zero, so this sprite has no effect.", MessageType.Info);
                        if (GUILayout.Button("Enable Pattern"))
                        { values.Get("patternOpacity").number = 1f; GUI.changed = true; }
                    }
                    if (!values.Boolean("outputAlbedo", true))
                        EditorGUILayout.HelpBox("To see the sprite's colors, choose Fabric Color > Generate Fabric Colors. With Keep Existing Color, only Pattern Height and Pattern Roughness can affect the material.", MessageType.Info);
                }
            }
        }

        internal static void SetClothPatternSprite(TexturePaintPluginParameterSet values, Sprite sprite)
        {
            var value = values.Get("patternSprite", true);
            bool firstAssignment = value.sprite == null && sprite != null;
            value.sprite = sprite;
            if (firstAssignment && values.Float("patternOpacity", 1f) <= 0f)
                values.Get("patternOpacity", true).number = 1f;
        }

        internal static bool IsClothMappingParameterVisible(TexturePaintPluginParameterSet values, string id)
        {
            if (!id.StartsWith("triplanar", StringComparison.Ordinal)) return true;
            if (values.Integer("projection", 0) != 1) return false;
            return values.Integer("triplanarBlend", 0) != 1 ||
                (id != "triplanarBlendOffset" && id != "triplanarBlendSharpness");
        }

        internal static void UseClothBaseColor(TexturePaintPluginParameterSet values)
        {
            values.Get("threadColor", true).color = values.Get("baseColor", true).color;
            values.Get("threadColorAmount", true).number = 0f;
        }

        private static int QuiltTileCount(TexturePaintPluginParameterSet values)
        {
            var set = values.Get("quiltSpriteSet")?.spriteSet;
            if (set == null) return Mathf.Max(1, values.Integer("atlasColumns", 4) * values.Integer("atlasRows", 4));
            foreach (var sheet in set.spriteSheets)
                if (sheet?.channel == TexturePaintChannel.Albedo)
                    return Mathf.Max(1, TexturePaintSpriteSetSource.GetOrderedSprites(sheet.spriteSheet).Count);
            return 1;
        }

        private static void DrawSpriteSet(GUIContent label, TexturePaintPluginParameterValue value,
            TexturePaintPluginParameterSet values)
        {
            var source = (OverlayPainterSpriteSet)EditorGUILayout.ObjectField(label, value.spriteSet, typeof(OverlayPainterSpriteSet), false);
            if (source != value.spriteSet)
            {
                value.spriteSet = source;
                value.spriteSetSelectionExplicit = false;
                value.enabledSpriteIndices = new List<int>();
                if (source != null)
                    foreach (var sheet in source.spriteSheets)
                    {
                        if (sheet == null) continue;
                        string id = sheet.channel == TexturePaintChannel.AmbientOcclusion ? "outputAO" : "output" + sheet.channel;
                        if (values.Get(id) != null) values.Get(id).boolean = true;
                    }
                values.Get("outputSpriteChannels", true).boolean = true;
            }
            if (source == null) return;
            try
            {
                int count = TexturePaintSpriteSetSource.Resolve(source)[TexturePaintChannel.Albedo].Count;
                EditorGUILayout.LabelField(value.spriteSetSelectionExplicit
                    ? (value.enabledSpriteIndices?.Count ?? 0) + " of " + count + " tiles enabled" : "All " + count + " tiles enabled", EditorStyles.miniLabel);
                bool changed = GUI.changed;
                if (GUILayout.Button("Select Tiles…"))
                {
                    int[] selection = QuiltSpriteSetTileWindow.Select(source, value);
                    GUI.changed = changed;
                    if (selection != null)
                    {
                        value.enabledSpriteIndices = new List<int>(selection);
                        value.spriteSetSelectionExplicit = true;
                        if (!value.enabledSpriteIndices.Contains(values.Integer("quiltAtlasCell", 1) - 1))
                            values.Get("quiltAtlasCell", true).number = selection[0] + 1;
                        GUI.changed = true;
                    }
                }
            }
            catch (InvalidOperationException exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Warning); }
        }

        private static void DrawStripeList(GUIContent label,
            TexturePaintPluginParameterValue value)
        {
            value.stripes ??= new List<TexturePaintStripeDefinition>();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            if (!string.IsNullOrWhiteSpace(label.tooltip))
                EditorGUILayout.LabelField(label.tooltip, EditorStyles.wordWrappedMiniLabel);
            for (int i = 0; i < value.stripes.Count; i++)
            {
                TexturePaintStripeDefinition stripe = value.stripes[i] ??=
                    new TexturePaintStripeDefinition();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();
                stripe.enabled = EditorGUILayout.Toggle(stripe.enabled, GUILayout.Width(18f));
                EditorGUILayout.LabelField((i + 1) + " · " + stripe.Rotation.ToString("0.#") + "° Stripe",
                    EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("▲", GUILayout.Width(26f)))
                    {
                        value.stripes.RemoveAt(i); value.stripes.Insert(i - 1, stripe);
                        GUI.changed = true;
                        GUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); return;
                    }
                using (new EditorGUI.DisabledScope(i == value.stripes.Count - 1))
                    if (GUILayout.Button("▼", GUILayout.Width(26f)))
                    {
                        value.stripes.RemoveAt(i); value.stripes.Insert(i + 1, stripe);
                        GUI.changed = true;
                        GUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); return;
                    }
                if (GUILayout.Button("×", GUILayout.Width(26f)))
                {
                    value.stripes.RemoveAt(i);
                    GUI.changed = true;
                    GUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); return;
                }
                GUILayout.EndHorizontal();
                using (new EditorGUI.DisabledScope(!stripe.enabled))
                {
                    EditorGUI.BeginChangeCheck();
                    float angle = EditorGUILayout.Slider(new GUIContent("Rotation",
                        "Angle within the plaid repeat grid: horizontal is 0°, vertical is 90°. Overall and Stripe Layout Rotation also apply."),
                        stripe.Rotation, -180f, 180f);
                    if (EditorGUI.EndChangeCheck()) stripe.Rotation = angle;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Horizontal (0°)", EditorStyles.miniButtonLeft))
                        { stripe.SetDirection(TexturePaintStripeDirection.Horizontal); GUI.changed = true; }
                        if (GUILayout.Button("Vertical (90°)", EditorStyles.miniButtonRight))
                        { stripe.SetDirection(TexturePaintStripeDirection.Vertical); GUI.changed = true; }
                    }
                    stripe.color = EditorGUILayout.ColorField("Color", stripe.color);
                    stripe.position = EditorGUILayout.Slider(new GUIContent("Position",
                        "Center within one repeat cell."), stripe.position, 0f, 1f);
                    stripe.width = EditorGUILayout.Slider(new GUIContent("Width",
                        "Fraction of one repeat cell."), stripe.width, 0.001f, 1f);
                    stripe.softness = EditorGUILayout.Slider("Edge Softness", stripe.softness,
                        0f, 0.25f);
                    stripe.opacity = EditorGUILayout.Slider("Opacity", stripe.opacity, 0f, 1f);
                }
                EditorGUILayout.EndVertical();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Vertical Stripe"))
            {
                value.stripes.Add(new TexturePaintStripeDefinition
                {
                    direction = TexturePaintStripeDirection.Vertical,
                    position = 0.5f, width = 0.12f, color = Color.white
                });
                GUI.changed = true;
            }
            if (GUILayout.Button("+ Horizontal Stripe"))
            {
                value.stripes.Add(new TexturePaintStripeDefinition
                {
                    direction = TexturePaintStripeDirection.Horizontal,
                    position = 0.5f, width = 0.12f, color = Color.white
                });
                GUI.changed = true;
            }
            GUILayout.EndHorizontal();
        }

        private TexturePaintPluginParameterSet GetParameters(ITexturePaintExtensionV2 plugin)
            => controller.Plugins.GetParameters(plugin);

        private async void RunCommand(ITexturePaintCommandExtensionV2 plugin, TexturePaintPluginParameterSet values)
        {
            Begin(plugin.Descriptor.displayName);
            try
            {
                await controller.Plugins.ExecuteCommandAsync(plugin, controller.Textures, values,
                    new Progress<float>(Report), cancellation.Token);
                SceneView.RepaintAll();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { End(); }
        }

        private async void RunBaker(ITexturePaintBakerV2 plugin, TexturePaintPluginParameterSet values)
        {
            Begin(plugin.Descriptor.displayName);
            try
            {
                TexturePaintPluginArtifact artifact = await controller.Plugins.ExecuteBakerAsync(plugin, controller.Textures,
                    values, new Progress<float>(Report), cancellation.Token);
                SaveArtifact(artifact);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { End(); }
        }

        private async void RunExporter(ITexturePaintExporterV2 plugin, TexturePaintPluginParameterSet values)
        {
            Begin(plugin.Descriptor.displayName);
            try
            {
                TexturePaintPluginArtifact artifact = await controller.Plugins.ExecuteExporterAsync(plugin, controller.Textures,
                    values, new Progress<float>(Report), cancellation.Token);
                SaveArtifact(artifact);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { End(); }
        }

        private async void RunImporter(ITexturePaintImporterV2 plugin, TexturePaintPluginParameterSet values)
        {
            string path = EditorUtility.OpenFilePanel("Import Plugin Artifact", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(path)) return;
            Begin(plugin.Descriptor.displayName);
            try
            {
                var artifact = new TexturePaintPluginArtifact
                {
                    name = Path.GetFileNameWithoutExtension(path), extension = Path.GetExtension(path).TrimStart('.'), bytes = File.ReadAllBytes(path)
                };
                await controller.Plugins.ExecuteImporterAsync(plugin, artifact, controller.Textures,
                    values, new Progress<float>(Report), cancellation.Token);
                SceneView.RepaintAll();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { End(); }
        }

        private void DrawDiagnostics()
        {
            diagnosticsExpanded = EditorGUILayout.Foldout(diagnosticsExpanded, "Diagnostics", true);
            if (!diagnosticsExpanded) return;
            IReadOnlyList<TexturePaintPluginDiagnostic> entries = controller.Plugins.Diagnostics;
            for (int i = Mathf.Max(0, entries.Count - 20); i < entries.Count; i++)
            {
                TexturePaintPluginDiagnostic entry = entries[i];
                MessageType type = entry.severity == TexturePaintPluginDiagnosticSeverity.Error ? MessageType.Error :
                    entry.severity == TexturePaintPluginDiagnosticSeverity.Warning ? MessageType.Warning : MessageType.None;
                string metrics = entry.durationMilliseconds > 0d ? $" ({entry.durationMilliseconds:0.0} ms, {entry.commandCount} commands, {entry.dirtyPixels} dirty px)" : string.Empty;
                EditorGUILayout.HelpBox(entry.pluginId + ": " + entry.message + metrics, type);
            }
            if (GUILayout.Button("Clear Diagnostics")) controller.Plugins.ClearDiagnostics();
        }

        private void Begin(string label)
        {
            cancellation = new CancellationTokenSource(); running = label; progress = 0f;
        }

        private void Report(float value) { progress = Mathf.Clamp01(value); Repaint(); }
        private void End() { cancellation?.Dispose(); cancellation = null; running = null; progress = 0f; Repaint(); }

        private static void SaveArtifact(TexturePaintPluginArtifact artifact)
        {
            if (artifact?.bytes == null) return;
            string extension = string.IsNullOrWhiteSpace(artifact.extension) ? "bin" : artifact.extension.TrimStart('.');
            string path = EditorUtility.SaveFilePanel("Save Plugin Artifact", string.Empty,
                string.IsNullOrWhiteSpace(artifact.name) ? "TexturePaintArtifact" : artifact.name, extension);
            if (!string.IsNullOrEmpty(path)) File.WriteAllBytes(path, artifact.bytes);
        }

        private void DisposePreviews()
        {
            foreach (var preview in commandPreviews.Values) preview.Dispose(); commandPreviews.Clear();
            foreach (var preview in brushPreviews.Values) preview.Dispose(); brushPreviews.Clear();
        }
        private void OnDisable() { DisposePreviews(); cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; }
    }
}
