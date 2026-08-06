using System;
using System.Collections.Generic;
using System.Threading;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed class TexturePaintExportWindow : EditorWindow
    {
        [SerializeField] private TexturePaintExportTemplate template;
        private TexturePaintStageController controller;
        private DynamicCharacterAvatar avatar;
        private TextureSet current;
        private TexturePaintStageState state;
        private TexturePaintDocument document;
        private TexturePaintExportPlan plan;
        private Vector2 scroll;
        private string status;
        [SerializeField] private string exportIdentifier;
        [SerializeField] private string identifierSessionKey;

        public static void Open(TexturePaintStageController controller, DynamicCharacterAvatar avatar,
            TextureSet current, TexturePaintStageState state, TexturePaintDocument document)
        {
            TexturePaintExportWindow window = GetWindow<TexturePaintExportWindow>("Overlay Painter Export");
            window.controller = controller; window.avatar = avatar; window.current = current;
            window.state = state ?? new TexturePaintStageState(); window.document = document;
            string sessionKey = document != null ? document.documentId : current?.persistentId;
            if (!string.Equals(window.identifierSessionKey, sessionKey, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(window.exportIdentifier))
            {
                string sessionName = document != null ? document.name :
                    current?.surface?.slotName ?? avatar?.name ?? "TexturePaint";
                window.exportIdentifier = TexturePaintExporter.CreateDefaultIdentifier(sessionName, DateTime.Now);
                window.identifierSessionKey = sessionKey;
            }
            window.LoadTemplate(); window.RebuildPlan(); window.Show();
        }

        private void LoadTemplate()
        {
            if (template != null) return;
            if (!string.IsNullOrEmpty(state?.exportTemplateGuid))
                template = AssetDatabase.LoadAssetAtPath<TexturePaintExportTemplate>(AssetDatabase.GUIDToAssetPath(state.exportTemplateGuid));
            if (template == null) UseDescriptorDefaults();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Recipe-ready UMA Export", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Export writes one physical texture per UMAMaterial channel and one indexed " +
                "OverlayDataAsset per slot or UDIM tile. It never changes the paint document, avatar, recipe, or " +
                "source assets unless Overwrite Source Overlay is explicitly enabled.", MessageType.Info);
            EditorGUILayout.HelpBox("Texture packing, encoding, importer type, color space, and normal convention " +
                "come from the UMAMaterial descriptors. A template asset is optional and only saves reusable " +
                "output overrides such as folder, naming policy, resolution, and padding.", MessageType.None);
            EditorGUI.BeginChangeCheck();
            TexturePaintExportTemplate selected = (TexturePaintExportTemplate)EditorGUILayout.ObjectField(
                "Override Template (Optional)", AssetDatabase.Contains(template) ? template : null,
                typeof(TexturePaintExportTemplate), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (selected != null) template = selected;
                else UseDescriptorDefaults();
                RememberTemplateSelection();
                RebuildPlan();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Overrides as Template")) CreateTemplate();
            if (GUILayout.Button("Reset Overrides"))
            {
                UseDescriptorDefaults();
                RememberTemplateSelection();
                RebuildPlan();
            }
            using (new EditorGUI.DisabledScope(template == null))
                if (GUILayout.Button("Refresh Preview")) RebuildPlan();
            GUILayout.EndHorizontal();
            if (template == null) return;

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            exportIdentifier = EditorGUILayout.TextField(new GUIContent("Export Identifier",
                "Required identifier appended to every new texture and overlay name."), exportIdentifier);
            template.outputFolder = EditorGUILayout.TextField("Output Folder", template.outputFolder);
            template.scope = (TexturePaintExportScope)EditorGUILayout.EnumPopup("Scope", template.scope);
            template.overwritePolicy = (TexturePaintOverwritePolicy)EditorGUILayout.EnumPopup(
                "Name Conflict", template.overwritePolicy);
            template.resolution = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("Resolution",
                "0 preserves each physical channel's resolved output resolution."), template.resolution));
            template.padding = EditorGUILayout.IntSlider("Albedo Padding", template.padding, 0, 64);
            template.markAddressable = EditorGUILayout.Toggle("Mark Addressable", template.markAddressable);
            EditorGUILayout.Space(3f);
            template.overwriteSourceOverlay = EditorGUILayout.ToggleLeft(new GUIContent(
                "Overwrite Source Overlay",
                "Advanced destructive mode. Available only when every member has a persistent source overlay and texture set."),
                template.overwriteSourceOverlay);
            if (template.overwriteSourceOverlay)
                EditorGUILayout.HelpBox("The exact source overlays and texture files listed below will be replaced. " +
                    "A second confirmation is required and the transaction restores backups on failure.", MessageType.Warning);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(template);
                RebuildPlan();
            }
            EditorGUILayout.Space(8f);
            DrawMaterialCapabilities();
            DrawBindingReports();
            DrawPlan();
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(plan == null || !plan.IsValid || controller?.Textures == null))
                if (GUILayout.Button("Export Transaction", GUILayout.Height(28f))) Execute();
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
        }

        private void DrawBindingReports()
        {
            if (document == null || controller?.Textures == null) return;
            List<TexturePaintBindingReport> reports = TexturePaintDocumentStorage.AnalyzeBindings(document, controller.Textures);
            bool hasAttention = reports.Exists(report => report.status != TexturePaintBindingStatus.Exact);
            if (!hasAttention) return;
            EditorGUILayout.LabelField("Surface Binding", EditorStyles.boldLabel);
            for (int i = 0; i < reports.Count; i++)
            {
                TexturePaintBindingReport report = reports[i];
                if (report.status == TexturePaintBindingStatus.Exact) continue;
                MessageType type = report.status == TexturePaintBindingStatus.Orphaned ? MessageType.Error : MessageType.Warning;
                EditorGUILayout.HelpBox(report.materialName + " — " + report.status + ": " + report.message, type);
            }
        }

        private void DrawMaterialCapabilities()
        {
            if (controller?.Textures == null) return;
            EditorGUILayout.LabelField("Material Capability Preflight", EditorStyles.boldLabel);
            IReadOnlyList<TextureSet> sets = controller.Textures.Sets;
            for (int setIndex = 0; setIndex < sets.Count; setIndex++)
            {
                TextureSet set = sets[setIndex];
                if (current != null && template != null &&
                    template.scope == TexturePaintExportScope.CurrentMaterial && set != current) continue;
                TexturePaintMaterialCapabilityDescriptor descriptor = set.materialCapability;
                if (descriptor == null)
                {
                    EditorGUILayout.HelpBox(set.Name + " has no compiled material descriptor.", MessageType.Error);
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(set.Name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Pipeline", descriptor.pipeline.ToString());
                EditorGUILayout.LabelField("Shader", descriptor.shader != null ? descriptor.shader.name : "Missing");
                for (int channelIndex = 0; channelIndex < descriptor.Channels.Count; channelIndex++)
                {
                    TexturePaintMaterialChannelCapability channel = descriptor.Channels[channelIndex];
                    string rgba = $"R: {channel.layout.red}   G: {channel.layout.green}   " +
                                  $"B: {channel.layout.blue}   A: {channel.layout.alpha}";
                    UMAMaterial.TextureChannelOutputSettings resolvedOutput = channel.output;
                    string output = $"{channel.output.encoding} · {channel.output.importerType} · " +
                                    $"{channel.output.colorSpace} · " +
                                    $"{(channel.output.generateMipMaps ? "Mipmaps" : "No Mipmaps")}";
                    output += $"  Normals: {resolvedOutput.normalConvention}";
                    if (resolvedOutput.platformOverrides != null &&
                        resolvedOutput.platformOverrides.Length > 0)
                        output += $"  Platform overrides: {resolvedOutput.platformOverrides.Length}";
                    EditorGUILayout.LabelField($"{channel.index}: " +
                        (string.IsNullOrEmpty(channel.materialProperty) ? "Non-shader" : channel.materialProperty),
                        EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(rgba, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(output, EditorStyles.miniLabel);
                    for (int diagnosticIndex = 0; diagnosticIndex < channel.Diagnostics.Count; diagnosticIndex++)
                    {
                        TexturePaintCapabilityDiagnostic diagnostic = channel.Diagnostics[diagnosticIndex];
                        MessageType type = diagnostic.severity == TexturePaintCapabilitySeverity.Error
                            ? MessageType.Error
                            : diagnostic.severity == TexturePaintCapabilitySeverity.Warning
                                ? MessageType.Warning
                                : MessageType.Info;
                        EditorGUILayout.HelpBox($"[{diagnostic.code}] {diagnostic.message}", type);
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.Space(5f);
        }

        private void DrawPlan()
        {
            EditorGUILayout.LabelField("Resolved Output Preview", EditorStyles.boldLabel);
            if (plan == null) { EditorGUILayout.LabelField("No preview."); return; }
            for (int i = 0; i < plan.errors.Count; i++) EditorGUILayout.HelpBox(plan.errors[i], MessageType.Error);
            for (int i = 0; i < plan.warnings.Count; i++) EditorGUILayout.HelpBox(plan.warnings[i], MessageType.Warning);
            if (plan.entries.Count == 0) EditorGUILayout.HelpBox("No outputs are selected.", MessageType.Warning);
            for (int i = 0; i < plan.entries.Count; i++)
            {
                TexturePaintExportPlanEntry entry = plan.entries[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(entry.DisplayName, GUILayout.Width(210f));
                GUILayout.Label(entry.resolution + " x " + entry.resolution, GUILayout.Width(90f));
                EditorGUILayout.SelectableLabel(entry.path, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                GUILayout.EndHorizontal();
            }
            for (int i = 0; i < plan.overlays.Count; i++)
            {
                TexturePaintOverlayPlanEntry overlay = plan.overlays[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(overlay.DisplayName, GUILayout.Width(210f));
                EditorGUILayout.SelectableLabel(overlay.path, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                GUILayout.EndHorizontal();
            }
            if (plan.entries.Count > 0)
            {
                TexturePaintExportPlanEntry first = plan.entries[0];
                Texture preview = !string.IsNullOrEmpty(first.materialProperty) &&
                    first.set.physicalChannelGroups.TryGetValue(first.materialProperty,
                        out TexturePhysicalChannelGroup physical)
                    ? physical.packed
                    : first.set.GetChannel(first.materialChannel.LogicalChannels.Count > 0
                        ? first.materialChannel.LogicalChannels[0]
                        : TexturePaintChannel.Custom)?.PreviewTexture;
                if (preview != null)
                {
                    Rect rect = GUILayoutUtility.GetRect(128f, 128f, GUILayout.ExpandWidth(false));
                    EditorGUI.DrawPreviewTexture(rect, preview, null, ScaleMode.ScaleToFit);
                }
            }
        }

        private void RebuildPlan()
        {
            plan = controller?.Textures != null && template != null
                ? TexturePaintExporter.BuildPlan(controller.Textures, current,
                    avatar != null ? avatar.name : current?.surface?.slotName ?? "TexturePaint", template,
                    exportIdentifier, controller.LogicalTargets)
                : null;
            Repaint();
        }

        private void Execute()
        {
            bool overwriteConfirmed = false;
            if (template.overwriteSourceOverlay)
            {
                string affected = string.Join("\n", CollectAffectedSourcePaths());
                overwriteConfirmed = EditorUtility.DisplayDialog("Overwrite Source Overlay?",
                    "This replaces the following source assets:\n\n" + affected +
                    "\n\nBackups will be restored if the transaction fails, but a successful export intentionally " +
                    "changes these assets.", "Overwrite Source Assets", "Cancel");
                if (!overwriteConfirmed) return;
            }
            CancellationTokenSource cancellation = new CancellationTokenSource();
            Action<string, float> progress = (message, value) =>
            {
                if (EditorUtility.DisplayCancelableProgressBar("Texture Export", message, value))
                    cancellation.Cancel();
            };
            try
            {
                string documentRevision = document?.revisionId;
                bool documentWasDirty = document != null && EditorUtility.IsDirty(document);
                TexturePaintExportResult result = TexturePaintExporter.Export(controller.Textures, current, avatar,
                    template, state, exportIdentifier, controller.LogicalTargets, overwriteConfirmed,
                    new TexturePaintOperationContext(cancellation.Token), progress);
                if (document != null && (!string.Equals(documentRevision, document.revisionId, StringComparison.Ordinal) ||
                    documentWasDirty != EditorUtility.IsDirty(document)))
                    throw new InvalidOperationException("Export changed the paint document state unexpectedly.");
                SessionState.SetString("UMA.TexturePaint.LastExportResult",
                    string.Join(";", result.overlayPaths));
                status = $"Exported {result.TextureCount} physical textures and {result.overlayPaths.Count} " +
                    "indexed UMA overlays in {result.resultSets.Count} result sets.";
                RebuildPlan();
            }
            catch (OperationCanceledException) { status = "Export cancelled; transaction rolled back."; }
            catch (Exception exception) { status = "Export failed and was rolled back: " + exception.Message; Debug.LogException(exception); }
            finally { EditorUtility.ClearProgressBar(); cancellation.Dispose(); }
        }

        private void CreateTemplate()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Overlay Painter Export Template", "Overlay Painter Export Template", "asset", "Choose a template asset path.");
            if (string.IsNullOrEmpty(path)) return;
            TexturePaintExportTemplate saved = CreateInstance<TexturePaintExportTemplate>();
            if (template != null) EditorUtility.CopySerialized(template, saved);
            saved.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(saved, path);
            AssetDatabase.SaveAssetIfDirty(saved);
            template = saved;
            RememberTemplateSelection();
            RebuildPlan();
        }

        private void UseDescriptorDefaults()
        {
            template = CreateInstance<TexturePaintExportTemplate>();
            template.name = "Descriptor-derived session defaults";
            template.hideFlags = HideFlags.HideAndDontSave;
        }

        private void RememberTemplateSelection()
        {
            if (state == null) return;
            string path = template != null && AssetDatabase.Contains(template)
                ? AssetDatabase.GetAssetPath(template) : string.Empty;
            state.exportTemplateGuid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        }

        private List<string> CollectAffectedSourcePaths()
        {
            List<string> paths = new List<string>();
            if (plan == null) return paths;
            for (int i = 0; i < plan.entries.Count; i++)
                if (!paths.Contains(plan.entries[i].path)) paths.Add(plan.entries[i].path);
            for (int i = 0; i < plan.overlays.Count; i++)
                if (!paths.Contains(plan.overlays[i].path)) paths.Add(plan.overlays[i].path);
            return paths;
        }
    }

    [CustomEditor(typeof(TexturePaintExportTemplate))]
    internal sealed class TexturePaintExportTemplateEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("This optional asset stores reusable export overrides. Physical texture packing, " +
                "encoding, color space, and importer behavior still come from the UMAMaterial descriptors. " +
                "Logical/custom diagnostic files, material overrides, and implicit recipe updates are not part of " +
                "the release export path.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("outputFolder"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("scope"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("overwritePolicy"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("resolution"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("padding"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("markAddressable"));
            EditorGUILayout.Space(3f);
            SerializedProperty overwrite = serializedObject.FindProperty("overwriteSourceOverlay");
            EditorGUILayout.PropertyField(overwrite);
            if (overwrite.boolValue)
                EditorGUILayout.HelpBox("This mode replaces persistent source overlays and textures after a " +
                    "separate confirmation in the export window.", MessageType.Warning);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
