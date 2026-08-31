using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint
{
    public sealed class PluginHost : IDisposable
    {
        public TexturePaintLogicalLayerController LogicalLayers { get; set; }
        private readonly List<ITexturePaintExtensionV2> extensions = new List<ITexturePaintExtensionV2>();
        private readonly List<ITexturePaintBrushV2> brushes = new List<ITexturePaintBrushV2>();
        private readonly List<ITexturePaintCommandExtensionV2> commands = new List<ITexturePaintCommandExtensionV2>();
        private readonly List<ITexturePaintBakerV2> bakers = new List<ITexturePaintBakerV2>();
        private readonly List<ITexturePaintImporterV2> importers = new List<ITexturePaintImporterV2>();
        private readonly List<ITexturePaintExporterV2> exporters = new List<ITexturePaintExporterV2>();
        private readonly List<ScriptableObject> ownedInstances = new List<ScriptableObject>();
        private readonly List<TexturePaintPluginDiagnostic> diagnostics = new List<TexturePaintPluginDiagnostic>();
        private readonly List<TexturePaintPluginCommit> undo = new List<TexturePaintPluginCommit>();
        private readonly List<TexturePaintPluginCommit> redo = new List<TexturePaintPluginCommit>();
        private long commitVersion;
        private readonly Dictionary<string, TexturePaintPluginParameterSet> parameterProfiles = new Dictionary<string, TexturePaintPluginParameterSet>(StringComparer.Ordinal);
        private readonly Dictionary<string, TexturePaintPluginDescriptor> profileDescriptors = new Dictionary<string, TexturePaintPluginDescriptor>(StringComparer.Ordinal);

        public IReadOnlyList<ITexturePaintExtensionV2> Extensions => extensions;
        public IReadOnlyList<ITexturePaintBrushV2> Brushes => brushes;
        public IReadOnlyList<ITexturePaintCommandExtensionV2> Commands => commands;
        public IReadOnlyList<ITexturePaintBakerV2> Bakers => bakers;
        public IReadOnlyList<ITexturePaintImporterV2> Importers => importers;
        public IReadOnlyList<ITexturePaintExporterV2> Exporters => exporters;
        public IReadOnlyList<TexturePaintPluginDiagnostic> Diagnostics => diagnostics;
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public long CommitVersion => commitVersion;
        public long CommandMemoryBudgetBytes { get; set; } = 384L * 1024L * 1024L;
        public long SnapshotMemoryBudgetBytes { get; set; } = 512L * 1024L * 1024L;
        public long ArtifactMemoryBudgetBytes { get; set; } = 512L * 1024L * 1024L;
        public int HistoryCapacity { get; set; } = 20;
        public ComputeShader GpuGeneratorShader { get; set; }
        public event Action Changed;

        public void Discover()
        {
            List<TexturePaintPluginProfile> savedProfiles = CaptureProfiles();
            DisposeInstances();
            diagnostics.Clear();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type type in TexturePaintTypeDiscovery.GetTypesDerivedFrom<ITexturePaintExtensionV2>())
            {
                // Test helpers and implementation details often implement the plugin interfaces so they can
                // be passed directly to the host. Only externally visible types are registrations; private or
                // internal implementations are not part of the discoverable plugin catalog.
                if (!type.IsVisible || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters) continue;
                try
                {
                    ITexturePaintExtensionV2 instance = Create(type);
                    ValidateDescriptor(instance, type);
                    if (!ids.Add(instance.Descriptor.id)) throw new InvalidOperationException("Duplicate plugin id: " + instance.Descriptor.id);
                    Register(instance);
                    AddDiagnostic(instance.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Info,
                        $"Loaded API v{instance.Descriptor.apiVersion} plugin {instance.Descriptor.pluginVersion}.");
                }
                catch (Exception exception)
                {
                    AddDiagnostic(type.FullName, TexturePaintPluginDiagnosticSeverity.Error,
                        "Plugin registration failed.", exception);
                }
            }
            RestoreProfiles(savedProfiles);
        }

        public TexturePaintPluginParameterSet CreateParameters(ITexturePaintExtensionV2 plugin)
        {
            TexturePaintPluginParameterSet set = new TexturePaintPluginParameterSet();
            set.ResetToDefaults(plugin?.Descriptor);
            return set;
        }

        public TexturePaintPluginParameterSet GetParameters(ITexturePaintExtensionV2 plugin)
        {
            if (plugin == null) return new TexturePaintPluginParameterSet();
            profileDescriptors[plugin.Descriptor.id] = plugin.Descriptor;
            if (!parameterProfiles.TryGetValue(plugin.Descriptor.id, out TexturePaintPluginParameterSet parameters))
                parameterProfiles.Add(plugin.Descriptor.id, parameters = CreateParameters(plugin));
            return parameters;
        }

        public ITexturePaintCommandExtensionV2 FindCommand(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return null;
            for (int i = 0; i < commands.Count; i++)
                if (string.Equals(commands[i]?.Descriptor?.id, pluginId, StringComparison.Ordinal))
                    return commands[i];
            return null;
        }

        public TexturePaintPluginParameterSet GetLayerParameters(TexturePaintLayer layer,
            ITexturePaintCommandExtensionV2 plugin)
        {
            if (layer == null || plugin == null) return new TexturePaintPluginParameterSet();
            if (!string.Equals(layer.pluginId, plugin.Descriptor.id, StringComparison.Ordinal) ||
                layer.pluginParameters == null)
                return CreateParameters(plugin);
            return layer.pluginParameters.Clone();
        }

        public List<TexturePaintPluginProfile> CaptureProfiles()
        {
            var profiles = new List<TexturePaintPluginProfile>();
            foreach (KeyValuePair<string, TexturePaintPluginParameterSet> pair in parameterProfiles)
                profiles.Add(new TexturePaintPluginProfile
                {
                    pluginId = pair.Key,
                    parameters = CloneParameters(pair.Value)
                });
            return profiles;
        }

        public void RestoreProfiles(IReadOnlyList<TexturePaintPluginProfile> profiles)
        {
            if (profiles == null) return;
            for (int i = 0; i < profiles.Count; i++)
            {
                TexturePaintPluginProfile profile = profiles[i];
                if (profile == null || string.IsNullOrEmpty(profile.pluginId) || profile.parameters == null) continue;
                TexturePaintPluginDescriptor descriptor = null;
                if (!profileDescriptors.TryGetValue(profile.pluginId, out descriptor))
                    for (int pluginIndex = 0; pluginIndex < extensions.Count; pluginIndex++)
                        if (extensions[pluginIndex].Descriptor.id == profile.pluginId) { descriptor = extensions[pluginIndex].Descriptor; break; }
                if (descriptor == null) continue;
                try
                {
                    TexturePaintPluginParameterSet copy = CloneParameters(profile.parameters);
                    ValidateParameters(descriptor, copy);
                    parameterProfiles[profile.pluginId] = copy;
                }
                catch (Exception exception)
                {
                    AddDiagnostic(profile.pluginId, TexturePaintPluginDiagnosticSeverity.Warning,
                        "Saved parameter profile was incompatible with the current schema and was ignored.", exception);
                }
            }
        }

        public TexturePaintBrushContextV2 BeginBrush(ITexturePaintBrushV2 plugin, string surfaceId,
            TexturePaintChannel channel, TexturePaintPluginParameterSet parameters, CancellationToken token)
        {
            if (plugin == null) return null;
            ValidateDescriptor(plugin, plugin.GetType());
            ValidateParameters(plugin.Descriptor, parameters);
            if (!plugin.Descriptor.Declares(channel)) throw new InvalidOperationException($"Plugin '{plugin.Descriptor.id}' did not declare {channel}.");
            var context = new TexturePaintBrushContextV2
            {
                surfaceId = surfaceId, channel = channel,
                parameters = CloneParameters(parameters ?? CreateParameters(plugin)), cancellationToken = token
            };
            try { plugin.OnStrokeStart(context); }
            catch (Exception exception)
            {
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Error, "Brush start failed.", exception);
                throw;
            }
            return context;
        }

        public void EvaluateBrush(ITexturePaintBrushV2 plugin, TexturePaintBrushContextV2 context,
            StrokeSample input, ref TexturePaintBrushSampleV2 output)
        {
            if (plugin == null || context == null) return;
            context.cancellationToken.ThrowIfCancellationRequested();
            try { plugin.EvaluateSample(context, input, ref output); }
            catch (Exception exception)
            {
                output.skip = true;
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Error, "Brush sample failed and was skipped.", exception);
            }
            if (!output.skip && (!IsFinite(output.color.r) || !IsFinite(output.color.g) || !IsFinite(output.color.b) ||
                !IsFinite(output.color.a) || !IsFinite(output.opacityMultiplier) || !IsFinite(output.sizeMultiplier) ||
                !IsFinite(output.rotationOffset)))
            {
                output.skip = true;
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Error,
                    "Brush produced a non-finite sample and was skipped.");
            }
        }

        public void EndBrush(ITexturePaintBrushV2 plugin, TexturePaintBrushContextV2 context, bool committed)
        {
            if (plugin == null || context == null) return;
            try { plugin.OnStrokeEnd(context, committed); }
            catch (Exception exception) { AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Error, "Brush cleanup failed.", exception); }
        }

        public async Task ExecuteCommandAsync(ITexturePaintCommandExtensionV2 plugin, TextureStore store,
            TexturePaintPluginParameterSet parameters,
            IProgress<float> progress, CancellationToken token)
        {
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));
            ValidateDescriptor(plugin, plugin.GetType());
            ValidateParameters(plugin.Descriptor, parameters);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                TexturePaintPluginParameterSet parameterSnapshot = CloneParameters(parameters);
                if (TryGetGpuGenerator(plugin, out ITexturePaintGpuGeneratorV2 gpuGenerator))
                {
                    TexturePaintPluginCommit gpuCommit =
                        TexturePaintPluginTransactionExecutor.CommitGpuGenerator(store,
                            plugin.Descriptor, gpuGenerator.GpuKernelName, GpuGeneratorShader,
                            token, progress, parameterSnapshot, LogicalLayers);
                    if (gpuCommit.commandCount > 0) PushCommit(gpuCommit); else gpuCommit.Dispose();
                    AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Info,
                        "GPU generator transaction committed.", null,
                        stopwatch.Elapsed.TotalMilliseconds, gpuCommit.commandCount,
                        gpuCommit.dirtyPixels);
                    if (gpuCommit.commandCount > 0) Changed?.Invoke();
                    return;
                }
                TexturePaintReadContextV2 read = TexturePaintPluginTransactionExecutor.Capture(store,
                    plugin.Descriptor, parameterSnapshot, token, progress, SnapshotMemoryBudgetBytes,
                    null, ResolveReadChannels(plugin, parameterSnapshot));
                var context = new TexturePaintCommandContextV2(plugin.Descriptor, read, CloneParameters(parameterSnapshot), token, progress, CommandMemoryBudgetBytes);
                await plugin.ExecuteAsync(context);
                token.ThrowIfCancellationRequested();
                IReadOnlyList<TexturePaintPluginTileCommand> queued = context.SealAndSnapshot();
                TexturePaintPluginCommit commit = TexturePaintPluginTransactionExecutor.Commit(store,
                    context.Descriptor, queued, token, progress, parameterSnapshot, LogicalLayers);
                if (commit.commandCount > 0) PushCommit(commit); else commit.Dispose();
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Info, "Transaction committed.",
                    null, stopwatch.Elapsed.TotalMilliseconds, commit.commandCount, commit.dirtyPixels);
                if (commit.commandCount > 0) Changed?.Invoke();
            }
            catch (OperationCanceledException)
            {
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Warning,
                    "Transaction cancelled before commit.", null, stopwatch.Elapsed.TotalMilliseconds);
                throw;
            }
            catch (Exception exception)
            {
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Error,
                    "Transaction failed; no plugin changes were committed.", exception, stopwatch.Elapsed.TotalMilliseconds);
                throw;
            }
        }

        public async Task ExecutePluginLayerAsync(ITexturePaintCommandExtensionV2 plugin,
            TextureStore store, TexturePaintPluginParameterSet parameters,
            IReadOnlyDictionary<TextureSet, TexturePaintLayer> destinationLayers,
            IProgress<float> progress, CancellationToken token, bool recordHistory = true)
        {
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));
            ValidateDescriptor(plugin, plugin.GetType());
            ValidateParameters(plugin.Descriptor, parameters);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                TexturePaintPluginParameterSet parameterSnapshot = CloneParameters(parameters);
                if (TryGetGpuGenerator(plugin, out ITexturePaintGpuGeneratorV2 gpuGenerator))
                {
                    TexturePaintPluginCommit gpuCommit =
                        TexturePaintPluginTransactionExecutor.CommitGpuGeneratorIntoPluginLayers(
                            store, plugin.Descriptor, gpuGenerator.GpuKernelName,
                            GpuGeneratorShader, destinationLayers, token, progress,
                            parameterSnapshot);
                    bool gpuChanged = gpuCommit.hasChanges;
                    if (gpuChanged && recordHistory) PushCommit(gpuCommit); else gpuCommit.Dispose();
                    AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Info,
                        "GPU Plugin layer regenerated.", null,
                        stopwatch.Elapsed.TotalMilliseconds, gpuCommit.commandCount,
                        gpuCommit.dirtyPixels);
                    if (gpuChanged) Changed?.Invoke();
                    return;
                }
                TexturePaintReadContextV2 read = TexturePaintPluginTransactionExecutor.Capture(store,
                    plugin.Descriptor, parameterSnapshot, token, progress, SnapshotMemoryBudgetBytes,
                    destinationLayers, ResolveReadChannels(plugin, parameterSnapshot));
                var context = new TexturePaintCommandContextV2(plugin.Descriptor, read,
                    CloneParameters(parameterSnapshot), token, progress, CommandMemoryBudgetBytes);
                await plugin.ExecuteAsync(context);
                token.ThrowIfCancellationRequested();
                IReadOnlyList<TexturePaintPluginTileCommand> queued = context.SealAndSnapshot();
                TexturePaintPluginCommit commit =
                    TexturePaintPluginTransactionExecutor.CommitIntoPluginLayers(store,
                        context.Descriptor, queued, destinationLayers, token, progress,
                        parameterSnapshot);
                bool changed = commit.hasChanges;
                if (changed && recordHistory) PushCommit(commit); else commit.Dispose();
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Info,
                    "Plugin layer regenerated.", null, stopwatch.Elapsed.TotalMilliseconds,
                    commit.commandCount, commit.dirtyPixels);
                if (changed) Changed?.Invoke();
            }
            catch (OperationCanceledException)
            {
                MarkPluginLayersStale(destinationLayers, null);
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Warning,
                    "Plugin layer regeneration cancelled; the previous cached result was retained.",
                    null, stopwatch.Elapsed.TotalMilliseconds);
                throw;
            }
            catch (Exception exception)
            {
                MarkPluginLayersStale(destinationLayers, exception.Message);
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Error,
                    "Plugin layer regeneration failed; the previous cached result was retained.",
                    exception, stopwatch.Elapsed.TotalMilliseconds);
                throw;
            }
        }

        public async Task ExecuteLayerMaskAsync(ITexturePaintCommandExtensionV2 plugin,
            TextureStore store, TexturePaintPluginParameterSet parameters,
            IReadOnlyDictionary<TextureSet, TexturePaintLayer> destinationLayers,
            IProgress<float> progress, CancellationToken token, bool recordHistory = true)
        {
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));
            ValidateDescriptor(plugin, plugin.GetType());
            if ((plugin.Descriptor.supportedTargets & TexturePaintPluginTarget.LayerMask) == 0)
                throw new InvalidOperationException(
                    $"Plugin '{plugin.Descriptor.displayName}' does not support Layer Mask output.");
            ValidateParameters(plugin.Descriptor, parameters);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                TexturePaintPluginParameterSet snapshot = CloneParameters(parameters);
                TexturePaintReadContextV2 read = TexturePaintPluginTransactionExecutor.Capture(store,
                    plugin.Descriptor, snapshot, token, progress, SnapshotMemoryBudgetBytes,
                    destinationLayers, TexturePaintChannelMask.None, true);
                var context = new TexturePaintCommandContextV2(plugin.Descriptor, read,
                    CloneParameters(snapshot), token, progress, CommandMemoryBudgetBytes,
                    TexturePaintPluginTarget.LayerMask);
                await plugin.ExecuteAsync(context);
                token.ThrowIfCancellationRequested();
                IReadOnlyList<TexturePaintPluginTileCommand> queued = context.SealAndSnapshot();
                TexturePaintPluginCommit commit =
                    TexturePaintPluginTransactionExecutor.CommitIntoLayerMasks(store,
                        context.Descriptor, queued, destinationLayers, token, progress, snapshot);
                bool changed = commit.hasChanges;
                if (changed && recordHistory) PushCommit(commit); else commit.Dispose();
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Info,
                    "Layer-mask plugin transaction committed.", null,
                    stopwatch.Elapsed.TotalMilliseconds, commit.commandCount, commit.dirtyPixels);
                if (changed) Changed?.Invoke();
            }
            catch (OperationCanceledException)
            {
                MarkLayerMasksStale(destinationLayers, null);
                throw;
            }
            catch (Exception exception)
            {
                MarkLayerMasksStale(destinationLayers, exception.Message);
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Error,
                    "Layer-mask plugin transaction failed; the previous mask was retained.",
                    exception, stopwatch.Elapsed.TotalMilliseconds);
                throw;
            }
        }

        private static void MarkPluginLayersStale(
            IReadOnlyDictionary<TextureSet, TexturePaintLayer> destinationLayers, string error)
        {
            if (destinationLayers == null) return;
            foreach (KeyValuePair<TextureSet, TexturePaintLayer> pair in destinationLayers)
            {
                if (pair.Value == null) continue;
                pair.Value.pluginStale = true;
                pair.Value.pluginLastError = error;
            }
        }

        private static void MarkLayerMasksStale(
            IReadOnlyDictionary<TextureSet, TexturePaintLayer> destinationLayers, string error)
        {
            if (destinationLayers == null) return;
            foreach (KeyValuePair<TextureSet, TexturePaintLayer> pair in destinationLayers)
            {
                if (pair.Value?.layerMask == null) continue;
                pair.Value.layerMask.pluginStale = true;
                pair.Value.layerMask.pluginLastError = error;
            }
        }

        public async Task<TexturePaintPluginArtifact> ExecuteBakerAsync(ITexturePaintBakerV2 plugin, TextureStore store,
            TexturePaintPluginParameterSet parameters, IProgress<float> progress, CancellationToken token)
            => await ExecuteArtifact(plugin, store, parameters, progress, token, true);

        public async Task<TexturePaintPluginArtifact> ExecuteExporterAsync(ITexturePaintExporterV2 plugin, TextureStore store,
            TexturePaintPluginParameterSet parameters, IProgress<float> progress, CancellationToken token)
            => await ExecuteArtifact(plugin, store, parameters, progress, token, false);

        public async Task ExecuteImporterAsync(ITexturePaintImporterV2 plugin, TexturePaintPluginArtifact artifact,
            TextureStore store, TexturePaintPluginParameterSet parameters,
            IProgress<float> progress, CancellationToken token)
        {
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));
            ValidateDescriptor(plugin, plugin.GetType());
            ValidateParameters(plugin.Descriptor, parameters);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                TexturePaintPluginParameterSet parameterSnapshot = CloneParameters(parameters);
                if (artifact?.bytes == null || artifact.bytes.LongLength > ArtifactMemoryBudgetBytes)
                    throw new InvalidOperationException("Import artifact is empty or exceeds the plugin artifact memory budget.");
                TexturePaintReadContextV2 read = TexturePaintPluginTransactionExecutor.Capture(store,
                    plugin.Descriptor, parameterSnapshot, token, progress, SnapshotMemoryBudgetBytes,
                    null, ResolveReadChannels(plugin, parameterSnapshot));
                var context = new TexturePaintCommandContextV2(plugin.Descriptor, read, CloneParameters(parameterSnapshot), token, progress, CommandMemoryBudgetBytes);
                await plugin.ImportAsync(artifact, context);
                token.ThrowIfCancellationRequested();
                IReadOnlyList<TexturePaintPluginTileCommand> queued = context.SealAndSnapshot();
                TexturePaintPluginCommit commit = TexturePaintPluginTransactionExecutor.Commit(store,
                    context.Descriptor, queued, token, progress, parameterSnapshot, LogicalLayers);
                if (commit.commandCount > 0) PushCommit(commit); else commit.Dispose();
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Info, "Import transaction committed.",
                    null, stopwatch.Elapsed.TotalMilliseconds, commit.commandCount, commit.dirtyPixels);
                if (commit.commandCount > 0) Changed?.Invoke();
            }
            catch (Exception exception)
            {
                AddDiagnostic(plugin.Descriptor.id, exception is OperationCanceledException ? TexturePaintPluginDiagnosticSeverity.Warning : TexturePaintPluginDiagnosticSeverity.Error,
                    exception is OperationCanceledException ? "Import cancelled." : "Import failed; no changes were committed.", exception,
                    stopwatch.Elapsed.TotalMilliseconds);
                throw;
            }
        }

        public bool Undo()
        {
            if (undo.Count == 0) return false;
            TexturePaintPluginCommit commit = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            commit.Undo(); redo.Add(commit); Changed?.Invoke(); return true;
        }

        public bool Redo()
        {
            if (redo.Count == 0) return false;
            TexturePaintPluginCommit commit = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1);
            commit.Redo(); undo.Add(commit); Changed?.Invoke(); return true;
        }

        public void ClearHistory()
        {
            for (int i = 0; i < undo.Count; i++) undo[i].Dispose();
            for (int i = 0; i < redo.Count; i++) redo[i].Dispose();
            undo.Clear();
            redo.Clear();
        }

        public void ClearRedo()
        {
            for (int i = 0; i < redo.Count; i++) redo[i].Dispose();
            redo.Clear();
        }

        public void ClearDiagnostics() => diagnostics.Clear();

        private async Task<TexturePaintPluginArtifact> ExecuteArtifact(ITexturePaintExtensionV2 plugin,
            TextureStore store, TexturePaintPluginParameterSet parameters, IProgress<float> progress,
            CancellationToken token, bool baker)
        {
            ValidateDescriptor(plugin, plugin.GetType());
            ValidateParameters(plugin.Descriptor, parameters); Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                TexturePaintPluginParameterSet parameterSnapshot = CloneParameters(parameters);
                TexturePaintReadContextV2 read = TexturePaintPluginTransactionExecutor.Capture(store,
                    plugin.Descriptor, parameterSnapshot, token, progress, SnapshotMemoryBudgetBytes,
                    null, ResolveReadChannels(plugin, parameterSnapshot));
                TexturePaintPluginArtifact artifact = baker
                    ? await ((ITexturePaintBakerV2)plugin).BakeAsync(read, parameterSnapshot, progress, token)
                    : await ((ITexturePaintExporterV2)plugin).ExportAsync(read, parameterSnapshot, progress, token);
                token.ThrowIfCancellationRequested();
                if (artifact?.bytes == null || artifact.bytes.LongLength > ArtifactMemoryBudgetBytes)
                    throw new InvalidOperationException("Plugin artifact is empty or exceeds the artifact memory budget.");
                AddDiagnostic(plugin.Descriptor.id, TexturePaintPluginDiagnosticSeverity.Info,
                    "Artifact generated.", null, stopwatch.Elapsed.TotalMilliseconds);
                return artifact;
            }
            catch (Exception exception)
            {
                AddDiagnostic(plugin.Descriptor.id, exception is OperationCanceledException ? TexturePaintPluginDiagnosticSeverity.Warning : TexturePaintPluginDiagnosticSeverity.Error,
                    exception is OperationCanceledException ? "Artifact generation cancelled." : "Artifact generation failed.", exception,
                    stopwatch.Elapsed.TotalMilliseconds);
                throw;
            }
        }

        private void Register(ITexturePaintExtensionV2 instance)
        {
            extensions.Add(instance);
            if (instance is ITexturePaintBrushV2 brush) brushes.Add(brush);
            if (instance is ITexturePaintCommandExtensionV2 command) commands.Add(command);
            if (instance is ITexturePaintBakerV2 baker) bakers.Add(baker);
            if (instance is ITexturePaintImporterV2 importer) importers.Add(importer);
            if (instance is ITexturePaintExporterV2 exporter) exporters.Add(exporter);
        }

        private bool TryGetGpuGenerator(ITexturePaintCommandExtensionV2 plugin,
            out ITexturePaintGpuGeneratorV2 generator)
        {
            generator = plugin as ITexturePaintGpuGeneratorV2;
            return generator != null && GpuGeneratorShader != null &&
                SystemInfo.supportsComputeShaders &&
                !string.IsNullOrWhiteSpace(generator.GpuKernelName) &&
                GpuGeneratorShader.HasKernel(generator.GpuKernelName) &&
                GpuGeneratorShader.IsSupported(
                    GpuGeneratorShader.FindKernel(generator.GpuKernelName));
        }

        private static TexturePaintChannelMask? ResolveReadChannels(
            ITexturePaintExtensionV2 plugin, TexturePaintPluginParameterSet parameters)
        {
            if (!(plugin is ITexturePaintDynamicChannelUsageV2 dynamicUsage)) return null;
            TexturePaintChannelMask resolved = dynamicUsage.ResolveReadChannels(parameters);
            TexturePaintChannelMask allowed = plugin.Descriptor.ResolvedReadChannels;
            if ((resolved & ~allowed) != 0)
                throw new InvalidOperationException(
                    $"Plugin '{plugin.Descriptor.id}' requested channels outside its declared read contract.");
            return resolved;
        }

        private ITexturePaintExtensionV2 Create(Type type)
        {
            if (typeof(ScriptableObject).IsAssignableFrom(type))
            {
                ScriptableObject value = ScriptableObject.CreateInstance(type); value.hideFlags = HideFlags.HideAndDontSave;
                ownedInstances.Add(value); return (ITexturePaintExtensionV2)value;
            }
            return (ITexturePaintExtensionV2)Activator.CreateInstance(type);
        }

        private static void ValidateDescriptor(ITexturePaintExtensionV2 plugin, Type type)
        {
            TexturePaintPluginDescriptor descriptor = plugin?.Descriptor ?? throw new InvalidOperationException("Descriptor is required.");
            if (descriptor.apiVersion < TexturePaintPluginApi.MinimumVersion || descriptor.apiVersion > TexturePaintPluginApi.CurrentVersion)
                throw new InvalidOperationException($"Unsupported API version {descriptor.apiVersion}.");
            if (string.IsNullOrWhiteSpace(descriptor.id) || !IsStableId(descriptor.id))
                throw new InvalidOperationException("Plugin id must be a stable lowercase reverse-DNS identifier.");
            if (string.IsNullOrWhiteSpace(descriptor.displayName)) throw new InvalidOperationException("Display name is required.");
            if (string.IsNullOrWhiteSpace(descriptor.pluginVersion) || !Version.TryParse(descriptor.pluginVersion, out _))
                throw new InvalidOperationException("Plugin version must be a numeric version such as 1.0.0.");
            const TexturePaintPluginCapability allCapabilities = TexturePaintPluginCapability.Brush |
                TexturePaintPluginCapability.Filter | TexturePaintPluginCapability.Generator |
                TexturePaintPluginCapability.Baker | TexturePaintPluginCapability.Importer |
                TexturePaintPluginCapability.Exporter | TexturePaintPluginCapability.ReadsMeshMaps |
                TexturePaintPluginCapability.LongRunning |
                TexturePaintPluginCapability.GpuAccelerated;
            if ((descriptor.capabilities & ~allCapabilities) != 0)
                throw new InvalidOperationException("Descriptor contains unknown capability flags.");
            if ((descriptor.declaredChannels & ~TexturePaintChannelMask.All) != 0)
                throw new InvalidOperationException("Descriptor contains unknown channel flags.");
            if ((descriptor.readChannels & ~TexturePaintChannelMask.All) != 0)
                throw new InvalidOperationException("Descriptor contains unknown read-channel flags.");
            if (descriptor.channelSnapshotMaximumResolution < 0 ||
                descriptor.channelSnapshotMaximumResolution > 16384)
                throw new InvalidOperationException("Channel snapshot maximum resolution must be 0 (native) or no greater than 16384.");
            if ((descriptor.requiredMeshMaps & ~TexturePaintMeshMapMask.All) != 0)
                throw new InvalidOperationException("Descriptor contains unknown mesh-map flags.");
            if (descriptor.supportedTargets == TexturePaintPluginTarget.None ||
                (descriptor.supportedTargets & ~TexturePaintPluginTarget.All) != 0)
                throw new InvalidOperationException("Descriptor contains no valid plugin targets.");
            if (descriptor.requiredMeshMaps != TexturePaintMeshMapMask.None &&
                (descriptor.capabilities & TexturePaintPluginCapability.ReadsMeshMaps) == 0)
                throw new InvalidOperationException("A plugin requesting mesh maps must declare ReadsMeshMaps.");
            TexturePaintPluginCapability actual = CapabilitiesOf(plugin);
            if (plugin is ITexturePaintCommandExtensionV2 && !(plugin is ITexturePaintFilterV2) && !(plugin is ITexturePaintGeneratorV2))
                throw new InvalidOperationException("Command plugins must implement the filter or generator extension point.");
            if ((descriptor.capabilities & actual) != actual)
                throw new InvalidOperationException($"Descriptor capabilities do not declare implemented extension points on {type.FullName}.");
            if ((actual & (TexturePaintPluginCapability.Brush | TexturePaintPluginCapability.Filter | TexturePaintPluginCapability.Generator |
                TexturePaintPluginCapability.Importer)) != 0 && descriptor.declaredChannels == TexturePaintChannelMask.None)
                throw new InvalidOperationException("Pixel-producing plugins must declare at least one channel.");
            if (descriptor.parameters == null) throw new InvalidOperationException("Parameter schema collection cannot be null.");
            HashSet<string> parameterIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < descriptor.parameters.Count; i++)
            {
                TexturePaintPluginParameterDefinition parameter = descriptor.parameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.id) || !parameterIds.Add(parameter.id))
                    throw new InvalidOperationException("Parameter ids must be non-empty and unique.");
                ValidateParameterDefinition(parameter);
            }
        }

        private static TexturePaintPluginCapability CapabilitiesOf(ITexturePaintExtensionV2 plugin)
        {
            TexturePaintPluginCapability result = TexturePaintPluginCapability.None;
            if (plugin is ITexturePaintBrushV2) result |= TexturePaintPluginCapability.Brush;
            if (plugin is ITexturePaintFilterV2) result |= TexturePaintPluginCapability.Filter;
            if (plugin is ITexturePaintGeneratorV2) result |= TexturePaintPluginCapability.Generator;
            if (plugin is ITexturePaintBakerV2) result |= TexturePaintPluginCapability.Baker;
            if (plugin is ITexturePaintImporterV2) result |= TexturePaintPluginCapability.Importer;
            if (plugin is ITexturePaintExporterV2) result |= TexturePaintPluginCapability.Exporter;
            if (plugin is ITexturePaintGpuGeneratorV2)
                result |= TexturePaintPluginCapability.GpuAccelerated;
            return result;
        }

        private static bool IsStableId(string id)
        {
            if (!id.Contains(".")) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (!(char.IsLower(c) || char.IsDigit(c) || c == '.' || c == '-')) return false;
            }
            return true;
        }

        private static void ValidateParameters(TexturePaintPluginDescriptor descriptor, TexturePaintPluginParameterSet values)
        {
            values ??= new TexturePaintPluginParameterSet();
            for (int i = 0; i < descriptor.parameters.Count; i++)
            {
                TexturePaintPluginParameterDefinition definition = descriptor.parameters[i];
                TexturePaintPluginParameterValue value = values.Get(definition.id);
                if (value == null) continue;
                if ((definition.type == TexturePaintPluginParameterType.Float || definition.type == TexturePaintPluginParameterType.Integer || definition.type == TexturePaintPluginParameterType.Enum) &&
                    (!IsFinite(value.number) || value.number < definition.minimum || value.number > definition.maximum))
                    throw new ArgumentOutOfRangeException(definition.id, $"Parameter must be between {definition.minimum} and {definition.maximum}.");
                if (definition.type == TexturePaintPluginParameterType.Enum && (value.number < 0 || value.number >= definition.enumOptions.Length))
                    throw new ArgumentOutOfRangeException(definition.id, "Enum parameter is outside its schema.");
                if (definition.type == TexturePaintPluginParameterType.Color &&
                    (!IsFinite(value.color.r) || !IsFinite(value.color.g) || !IsFinite(value.color.b) || !IsFinite(value.color.a)))
                    throw new ArgumentOutOfRangeException(definition.id, "Color parameter must contain only finite values.");
                if (definition.type == TexturePaintPluginParameterType.StripeList)
                    ValidateStripes(definition.id, value.stripes);
            }
        }

        private static void ValidateParameterDefinition(TexturePaintPluginParameterDefinition definition)
        {
            if (!IsFinite(definition.minimum) || !IsFinite(definition.maximum) || definition.minimum > definition.maximum)
                throw new InvalidOperationException($"Parameter '{definition.id}' has an invalid numeric range.");
            if (!IsFinite(definition.defaultNumber) || definition.defaultNumber < definition.minimum || definition.defaultNumber > definition.maximum)
                throw new InvalidOperationException($"Parameter '{definition.id}' has a default outside its numeric range.");
            if (!IsFinite(definition.defaultColor.r) || !IsFinite(definition.defaultColor.g) ||
                !IsFinite(definition.defaultColor.b) || !IsFinite(definition.defaultColor.a))
                throw new InvalidOperationException($"Parameter '{definition.id}' has a non-finite default color.");
            if (definition.type == TexturePaintPluginParameterType.StripeList)
                ValidateStripes(definition.id, definition.defaultStripes);
            if (definition.type != TexturePaintPluginParameterType.Enum) return;
            if (definition.enumOptions == null || definition.enumOptions.Length == 0)
                throw new InvalidOperationException($"Enum parameter '{definition.id}' requires options.");
            HashSet<string> options = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < definition.enumOptions.Length; i++)
                if (string.IsNullOrWhiteSpace(definition.enumOptions[i]) || !options.Add(definition.enumOptions[i]))
                    throw new InvalidOperationException($"Enum parameter '{definition.id}' options must be non-empty and unique.");
        }

        private static void ValidateStripes(string parameterId,
            IReadOnlyList<TexturePaintStripeDefinition> stripes)
        {
            if (stripes == null) return;
            if (stripes.Count > 64)
                throw new ArgumentOutOfRangeException(parameterId,
                    "A stripe-list parameter cannot contain more than 64 stripes.");
            for (int i = 0; i < stripes.Count; i++)
            {
                TexturePaintStripeDefinition stripe = stripes[i];
                if (stripe == null || !IsFinite(stripe.position) || !IsFinite(stripe.width) ||
                    !IsFinite(stripe.softness) || !IsFinite(stripe.opacity) ||
                    !IsFinite(stripe.color.r) || !IsFinite(stripe.color.g) ||
                    !IsFinite(stripe.color.b) || !IsFinite(stripe.color.a) ||
                    stripe.position < 0f || stripe.position > 1f || stripe.width <= 0f ||
                    stripe.width > 1f || stripe.softness < 0f || stripe.softness > 0.5f ||
                    stripe.opacity < 0f || stripe.opacity > 1f)
                    throw new ArgumentOutOfRangeException(parameterId,
                        $"Stripe {i + 1} contains invalid values.");
            }
        }

        private static TexturePaintPluginParameterSet CloneParameters(TexturePaintPluginParameterSet parameters)
            => parameters?.Clone() ?? new TexturePaintPluginParameterSet();

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void PushCommit(TexturePaintPluginCommit commit)
        {
            for (int i = 0; i < redo.Count; i++) redo[i].Dispose(); redo.Clear();
            undo.Add(commit);
            while (undo.Count > Mathf.Max(1, HistoryCapacity)) { undo[0].Dispose(); undo.RemoveAt(0); }
            commitVersion++;
        }

        private void AddDiagnostic(string pluginId, TexturePaintPluginDiagnosticSeverity severity, string message,
            Exception exception = null, double duration = 0d, int commandCount = 0, long dirtyPixels = 0L)
        {
            diagnostics.Add(new TexturePaintPluginDiagnostic
            {
                timestampUtc = DateTime.UtcNow, pluginId = pluginId, severity = severity, message = message,
                exception = exception?.ToString(), durationMilliseconds = duration,
                commandCount = commandCount, dirtyPixels = dirtyPixels
            });
            while (diagnostics.Count > 256) diagnostics.RemoveAt(0);
            if (severity == TexturePaintPluginDiagnosticSeverity.Error) UnityEngine.Debug.LogError($"Overlay Painter plugin {pluginId}: {message}\n{exception}");
            else if (severity == TexturePaintPluginDiagnosticSeverity.Warning) UnityEngine.Debug.LogWarning($"Overlay Painter plugin {pluginId}: {message}");
        }

        private void DisposeInstances()
        {
            extensions.Clear(); brushes.Clear(); commands.Clear(); bakers.Clear(); importers.Clear(); exporters.Clear();
            parameterProfiles.Clear();
            profileDescriptors.Clear();
            for (int i = 0; i < ownedInstances.Count; i++)
                if (ownedInstances[i] != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(ownedInstances[i]);
                    else UnityEngine.Object.DestroyImmediate(ownedInstances[i]);
                }
            ownedInstances.Clear();
        }

        public void Dispose()
        {
            DisposeInstances();
            for (int i = 0; i < undo.Count; i++) undo[i].Dispose(); undo.Clear();
            for (int i = 0; i < redo.Count; i++) redo[i].Dispose(); redo.Clear();
        }
    }

    internal static class TexturePaintTypeDiscovery
    {
        public static IEnumerable<Type> GetTypesDerivedFrom<T>()
        {
            Type contract = typeof(T);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                if (types == null) continue;
                for (int i = 0; i < types.Length; i++)
                    if (types[i] != null && contract.IsAssignableFrom(types[i])) yield return types[i];
            }
        }
    }
}
