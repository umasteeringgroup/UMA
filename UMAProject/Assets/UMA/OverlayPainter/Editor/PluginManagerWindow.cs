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

        public static void Open(TexturePaintStageController controller)
        {
            PluginManagerWindow window = GetWindow<PluginManagerWindow>("Overlay Painter Plugins");
            window.controller = controller; window.minSize = new Vector2(520f, 420f); window.Show();
        }

        private void OnGUI()
        {
            if (controller?.Plugins == null)
            {
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
            DrawCategory("Filters & Generators", controller.Plugins.Commands);
            DrawCategory("Bakers", controller.Plugins.Bakers);
            DrawCategory("Importers", controller.Plugins.Importers);
            DrawCategory("Exporters", controller.Plugins.Exporters);
            DrawDiagnostics();
            EditorGUILayout.EndScrollView();

            if (cancellation != null)
            {
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, running ?? "Running");
                if (GUILayout.Button("Cancel")) cancellation.Cancel();
                Repaint();
            }
            using (new EditorGUI.DisabledScope(cancellation != null))
                if (GUILayout.Button("Refresh Plugins")) controller.Plugins.Discover();
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
            EditorGUILayout.LabelField("Channels", descriptor.declaredChannels.ToString());
            TexturePaintPluginParameterSet values = GetParameters(plugin);
            DrawParameters(descriptor, values);
            using (new EditorGUI.DisabledScope(cancellation != null))
            {
                if (plugin is ITexturePaintCommandExtensionV2 command && GUILayout.Button("Run Transaction")) RunCommand(command, values);
                else if (plugin is ITexturePaintBakerV2 baker && GUILayout.Button("Bake Artifact...")) RunBaker(baker, values);
                else if (plugin is ITexturePaintImporterV2 importer && GUILayout.Button("Import Artifact...")) RunImporter(importer, values);
                else if (plugin is ITexturePaintExporterV2 exporter && GUILayout.Button("Export Artifact...")) RunExporter(exporter, values);
                else if (plugin is ITexturePaintBrushV2) EditorGUILayout.HelpBox("Select this brush in the Overlay Painter tool panel.", MessageType.None);
            }
            EditorGUILayout.EndVertical();
        }

        internal static void DrawParameters(TexturePaintPluginDescriptor descriptor, TexturePaintPluginParameterSet values)
        {
            for (int i = 0; i < descriptor.parameters.Count; i++)
            {
                TexturePaintPluginParameterDefinition definition = descriptor.parameters[i];
                TexturePaintPluginParameterValue value = values.Get(definition.id, true);
                GUIContent label = new GUIContent(string.IsNullOrEmpty(definition.displayName) ? definition.id : definition.displayName, definition.description);
                switch (definition.type)
                {
                    case TexturePaintPluginParameterType.Float:
                        value.number = EditorGUILayout.Slider(label, value.number, definition.minimum, definition.maximum); break;
                    case TexturePaintPluginParameterType.Integer:
                        value.number = EditorGUILayout.IntSlider(label, Mathf.RoundToInt(value.number), Mathf.RoundToInt(definition.minimum), Mathf.RoundToInt(definition.maximum)); break;
                    case TexturePaintPluginParameterType.Boolean:
                        value.boolean = EditorGUILayout.Toggle(label, value.boolean); break;
                    case TexturePaintPluginParameterType.Color:
                        value.color = EditorGUILayout.ColorField(label, value.color); break;
                    case TexturePaintPluginParameterType.Texture:
                        value.texture = (Texture2D)EditorGUILayout.ObjectField(label, value.texture, typeof(Texture2D), false); break;
                    case TexturePaintPluginParameterType.Enum:
                        value.number = EditorGUILayout.Popup(label, Mathf.Clamp(Mathf.RoundToInt(value.number), 0, Mathf.Max(0, definition.enumOptions.Length - 1)), definition.enumOptions); break;
                    default:
                        value.text = EditorGUILayout.TextField(label, value.text ?? string.Empty); break;
                }
            }
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

        private void OnDisable() { cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; }
    }
}
