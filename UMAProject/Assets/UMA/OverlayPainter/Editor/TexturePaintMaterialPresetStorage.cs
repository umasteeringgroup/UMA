using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    internal enum TexturePaintMaterialPresetIssueSeverity { Info, Warning, Error }

    internal sealed class TexturePaintMaterialPresetIssue
    {
        public TexturePaintMaterialPresetIssueSeverity severity;
        public string message;
    }

    internal sealed class TexturePaintMaterialPresetCompatibility
    {
        public readonly List<TexturePaintMaterialPresetIssue> issues =
            new List<TexturePaintMaterialPresetIssue>();
        public bool CanApply => !issues.Any(issue =>
            issue.severity == TexturePaintMaterialPresetIssueSeverity.Error);

        public string Summary(int maximumLines = 12)
        {
            if (issues.Count == 0) return "The preset is compatible with the selected paint target.";
            return string.Join("\n", issues.Take(Mathf.Max(1, maximumLines)).Select(issue =>
                (issue.severity == TexturePaintMaterialPresetIssueSeverity.Error ? "Error: " :
                    issue.severity == TexturePaintMaterialPresetIssueSeverity.Warning ? "Warning: " :
                    "Info: ") + issue.message)) +
                (issues.Count > maximumLines ? $"\n… and {issues.Count - maximumLines} more." : string.Empty);
        }
    }

    internal sealed class TexturePaintMaterialPresetApplyOptions
    {
        public bool wrapInGroup = true;
        public bool strictChannels;
        public bool strictPlugins;
    }

    internal sealed class TexturePaintMaterialPresetCreatedLayer
    {
        public TextureSet set;
        public TexturePaintLayer layer;
        public int index;
    }

    internal sealed class TexturePaintMaterialPresetApplyResult
    {
        public readonly List<TexturePaintMaterialPresetCreatedLayer> created =
            new List<TexturePaintMaterialPresetCreatedLayer>();
        public readonly List<string> warnings = new List<string>();
    }

    internal static class TexturePaintMaterialPresetStorage
    {
        public static void Capture(TexturePaintMaterialPreset preset, TextureSet source,
            IReadOnlyList<TexturePaintLayer> sourceLayers, bool wholeStack,
            PluginHost plugins, bool includeCachedPluginOutput = true)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (sourceLayers == null || sourceLayers.Count == 0)
                throw new InvalidOperationException("There are no layers to save in the material preset.");

            preset.Migrate();
            DateTime now = DateTime.UtcNow;
            if (string.IsNullOrEmpty(preset.createdUtc)) preset.createdUtc = now.ToString("O");
            preset.modifiedUtc = now.ToString("O");
            preset.revision = Mathf.Max(1, preset.revision + (preset.layers.Count > 0 ? 1 : 0));
            preset.displayName = string.IsNullOrWhiteSpace(preset.displayName) ? preset.name : preset.displayName;
            preset.sourceMaterialName = source.Name;
            preset.sourceSlotNames = source.surface?.slotNames != null
                ? new List<string>(source.surface.slotNames) : new List<string>();
            TexturePaintSurfaceFingerprint fingerprint =
                TexturePaintSurfaceFingerprintUtility.Compute(source.surface?.mesh);
            preset.sourceMeshSignature = fingerprint.geometry;
            preset.sourceTopologySignature = fingerprint.topology;
            preset.sourceUVSignature = fingerprint.uv;
            preset.includesWholeStack = wholeStack;
            preset.includesCachedPluginOutput = includeCachedPluginOutput;
            preset.packaged = false;
            preset.packagedFromPresetId = null;
            preset.packagedUtc = null;
            preset.packagedDependencies.Clear();
            preset.packagedExternalDependencies.Clear();
            preset.layers.Clear();
            preset.channels.Clear();
            preset.plugins.Clear();
            preset.portability = TexturePaintPresetPortability.Portable;

            var templateIds = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < sourceLayers.Count; i++)
            {
                TexturePaintLayer layer = sourceLayers[i];
                if (layer == null) continue;
                templateIds[layer.id] = Guid.NewGuid().ToString("N");
            }

            var channelPortability = new Dictionary<TexturePaintChannel, TexturePaintPresetPortability>();
            var pluginIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < sourceLayers.Count; i++)
            {
                TexturePaintLayer layer = sourceLayers[i];
                if (layer == null) continue;
                TexturePaintDocumentLayer saved = CaptureLayer(layer, includeCachedPluginOutput);
                saved.id = templateIds[layer.id];
                saved.logicalLayerId = null;
                saved.paintTargetId = null;
                saved.parentId = !string.IsNullOrEmpty(layer.parentId) &&
                    templateIds.TryGetValue(layer.parentId, out string parentTemplateId)
                        ? parentTemplateId : null;
                saved.proceduralGroupKey = null;
                preset.layers.Add(saved);

                TexturePaintPresetPortability layerPortability = PortabilityOf(layer);
                preset.portability |= layerPortability;
                foreach (TexturePaintChannel channel in layer.channels.Keys)
                {
                    if (!channelPortability.TryGetValue(channel, out TexturePaintPresetPortability current))
                        current = TexturePaintPresetPortability.Portable;
                    channelPortability[channel] = current | layerPortability;
                }
                AddPluginRequirement(preset, plugins, layer.pluginId, layer.pluginVersion,
                    TexturePaintPluginTarget.LayerContent, pluginIds);
                AddPluginRequirement(preset, plugins, layer.layerMask?.pluginId,
                    layer.layerMask?.pluginVersion,
                    TexturePaintPluginTarget.LayerMask, pluginIds);
            }

            foreach (KeyValuePair<TexturePaintChannel, TexturePaintPresetPortability> pair in
                     channelPortability.OrderBy(pair => (int)pair.Key))
                preset.channels.Add(new TexturePaintMaterialPresetChannel
                {
                    channel = pair.Key,
                    required = false,
                    portability = pair.Value
                });
            preset.Migrate();
        }

        public static TexturePaintMaterialPresetCompatibility Evaluate(
            TexturePaintMaterialPreset preset, IReadOnlyList<TextureSet> destinations,
            PluginHost plugins)
        {
            var report = new TexturePaintMaterialPresetCompatibility();
            if (preset == null)
            {
                report.issues.Add(Error("No material preset was selected."));
                return report;
            }
            preset.Migrate();
            if (preset.layers.Count == 0)
                report.issues.Add(Error("The material preset contains no layers."));
            if (destinations == null || destinations.Count == 0)
            {
                report.issues.Add(Error("The selected paint target has no texture sets."));
                return report;
            }

            for (int setIndex = 0; setIndex < destinations.Count; setIndex++)
            {
                TextureSet set = destinations[setIndex];
                if (set == null) continue;
                for (int channelIndex = 0; channelIndex < preset.channels.Count; channelIndex++)
                {
                    TexturePaintMaterialPresetChannel channel = preset.channels[channelIndex];
                    if (channel == null || set.GetChannel(channel.channel) != null) continue;
                    report.issues.Add(new TexturePaintMaterialPresetIssue
                    {
                        severity = channel.required ? TexturePaintMaterialPresetIssueSeverity.Error :
                            TexturePaintMaterialPresetIssueSeverity.Warning,
                        message = $"{set.Name} does not support {channel.channel}; that channel will be skipped."
                    });
                }
                TexturePaintSurfaceFingerprint fingerprint =
                    TexturePaintSurfaceFingerprintUtility.Compute(set.surface?.mesh);
                if ((preset.portability & TexturePaintPresetPortability.UVDependent) != 0 &&
                    !string.IsNullOrEmpty(preset.sourceUVSignature) &&
                    !string.Equals(preset.sourceUVSignature, fingerprint.uv, StringComparison.Ordinal))
                    report.issues.Add(Warning($"{set.Name} uses a different UV layout; raster paint and masks " +
                        "will be transferred in UV space."));
            }

            for (int i = 0; i < preset.plugins.Count; i++)
            {
                TexturePaintMaterialPresetPlugin requirement = preset.plugins[i];
                if (requirement == null || string.IsNullOrEmpty(requirement.pluginId)) continue;
                ITexturePaintCommandExtensionV2 plugin = plugins?.FindCommand(requirement.pluginId);
                if (plugin == null)
                    report.issues.Add(Warning($"Plugin '{requirement.pluginId}' is unavailable; its cached " +
                        "output will be retained and the layer marked stale."));
                else if (!string.IsNullOrEmpty(requirement.pluginVersion) &&
                    !string.Equals(requirement.pluginVersion, plugin.Descriptor.pluginVersion,
                        StringComparison.Ordinal))
                    report.issues.Add(Warning($"Plugin '{plugin.Descriptor.displayName}' was saved with " +
                        $"version {requirement.pluginVersion} and version {plugin.Descriptor.pluginVersion} is installed."));
            }
            for (int layerIndex = 0; layerIndex < preset.layers.Count; layerIndex++)
            {
                TexturePaintDocumentLayer layer = preset.layers[layerIndex];
                if (layer == null) continue;
                ValidateSource(layer.name, layer.fillSettings, report);
                if (layer.channels == null) continue;
                for (int channelIndex = 0; channelIndex < layer.channels.Count; channelIndex++)
                    ValidateSource(layer.name,
                        layer.channels[channelIndex]?.GetSourceSettings(), report);
            }
            return report;
        }

        public static async Task<TexturePaintMaterialPresetApplyResult> ApplyAsync(
            TexturePaintMaterialPreset preset, TextureStore store,
            IReadOnlyList<TextureSet> destinations, PluginHost plugins,
            TexturePaintLogicalTarget logicalTarget, TexturePaintMaterialPresetApplyOptions options,
            IProgress<float> progress, CancellationToken token)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (store == null) throw new ArgumentNullException(nameof(store));
            options ??= new TexturePaintMaterialPresetApplyOptions();
            preset.Migrate();
            List<TextureSet> sets = destinations?.Where(set => set != null).Distinct().ToList() ??
                new List<TextureSet>();
            TexturePaintMaterialPresetCompatibility compatibility = Evaluate(preset, sets, plugins);
            if (!compatibility.CanApply)
                throw new InvalidOperationException(compatibility.Summary());

            var result = new TexturePaintMaterialPresetApplyResult();
            var logicalIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var createdByTemplate = new Dictionary<string, Dictionary<TextureSet, TexturePaintLayer>>(
                StringComparer.Ordinal);
            for (int i = 0; i < preset.layers.Count; i++)
                if (preset.layers[i] != null)
                    logicalIds[preset.layers[i].id] = Guid.NewGuid().ToString("N");
            string wrapperLogicalId = Guid.NewGuid().ToString("N");

            try
            {
                int structuralWork = Mathf.Max(1, sets.Count * preset.layers.Count);
                int completedStructuralWork = 0;
                for (int setIndex = 0; setIndex < sets.Count; setIndex++)
                {
                    TextureSet set = sets[setIndex];
                    var physicalIds = new Dictionary<string, string>(StringComparer.Ordinal);
                    string wrapperId = null;
                    if (options.wrapInGroup)
                    {
                        TexturePaintLayer wrapper = NewLayer(preset.displayName ?? preset.name,
                            TexturePaintLayerKind.Group);
                        wrapper.logicalLayerId = wrapperLogicalId;
                        wrapper.paintTargetId = logicalTarget?.id;
                        wrapper.sourceMaterialPresetId = preset.presetId;
                        wrapper.sourceMaterialPresetRevision = preset.revision;
                        wrapper.sourceMaterialPresetLayerId = "__preset_root__";
                        set.layers.Add(wrapper);
                        wrapperId = wrapper.id;
                        result.created.Add(Location(set, wrapper));
                    }

                    for (int layerIndex = 0; layerIndex < preset.layers.Count; layerIndex++)
                    {
                        token.ThrowIfCancellationRequested();
                        TexturePaintDocumentLayer saved = preset.layers[layerIndex];
                        if (saved == null) continue;
                        TexturePaintLayer layer = RestoreLayer(set, saved, options, result.warnings);
                        layer.logicalLayerId = logicalIds[saved.id];
                        layer.paintTargetId = logicalTarget?.id;
                        layer.sourceMaterialPresetId = preset.presetId;
                        layer.sourceMaterialPresetRevision = preset.revision;
                        layer.sourceMaterialPresetLayerId = saved.id;
                        physicalIds[saved.id] = layer.id;
                        if (!createdByTemplate.TryGetValue(saved.id,
                                out Dictionary<TextureSet, TexturePaintLayer> peers))
                            createdByTemplate.Add(saved.id,
                                peers = new Dictionary<TextureSet, TexturePaintLayer>());
                        peers[set] = layer;
                        set.layers.Add(layer);
                        result.created.Add(Location(set, layer));
                        completedStructuralWork++;
                        progress?.Report(completedStructuralWork / (float)(structuralWork + 1));
                    }

                    for (int layerIndex = 0; layerIndex < preset.layers.Count; layerIndex++)
                    {
                        TexturePaintDocumentLayer saved = preset.layers[layerIndex];
                        if (saved == null || !createdByTemplate.TryGetValue(saved.id, out var peers) ||
                            !peers.TryGetValue(set, out TexturePaintLayer layer)) continue;
                        layer.parentId = !string.IsNullOrEmpty(saved.parentId) &&
                            physicalIds.TryGetValue(saved.parentId, out string parentId)
                                ? parentId : wrapperId;
                        if (layer.kind == TexturePaintLayerKind.Fill)
                        {
                            if (HasMissingFillSource(layer))
                                result.warnings.Add($"{set.Name}: '{layer.name}' has a missing source; cached pixels were retained.");
                            else if (!set.RegenerateFillLayer(layer))
                            {
                                RestoreLayerChannelPixels(saved, layer);
                                result.warnings.Add($"{set.Name}: '{layer.name}' could not regenerate; cached pixels were retained.");
                            }
                        }
                    }
                    set.NormalizeLayerHierarchy();
                    set.activeLayerIndex = options.wrapInGroup
                        ? set.layers.FindIndex(layer => layer.id == wrapperId)
                        : set.layers.Count - 1;
                    set.BindPreviewTextures();
                }

                int proceduralCount = preset.layers.Count(layer => layer != null &&
                    (!string.IsNullOrEmpty(layer.pluginId) || !string.IsNullOrEmpty(layer.maskPluginId)));
                int completedProcedural = 0;
                for (int layerIndex = 0; layerIndex < preset.layers.Count; layerIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    TexturePaintDocumentLayer saved = preset.layers[layerIndex];
                    if (saved == null || !createdByTemplate.TryGetValue(saved.id,
                            out Dictionary<TextureSet, TexturePaintLayer> peers)) continue;
                    if (!string.IsNullOrEmpty(saved.pluginId))
                    {
                        ITexturePaintCommandExtensionV2 plugin = plugins?.FindCommand(saved.pluginId);
                        if (plugin == null)
                        {
                            string warning = $"Plugin '{saved.pluginId}' is unavailable; cached output was retained.";
                            if (options.strictPlugins) throw new InvalidOperationException(warning);
                            result.warnings.Add(warning);
                        }
                        else
                        {
                            await plugins.ExecutePluginLayerAsync(plugin, store,
                                saved.pluginParameters?.Clone() ?? plugins.CreateParameters(plugin), peers,
                                ProgressSlice(progress, completedStructuralWork, structuralWork,
                                    completedProcedural, proceduralCount), token, false);
                            RefreshReplacedDestinations(peers, result.created);
                        }
                    }
                    if (!string.IsNullOrEmpty(saved.maskPluginId))
                    {
                        ITexturePaintCommandExtensionV2 plugin = plugins?.FindCommand(saved.maskPluginId);
                        if (plugin == null)
                        {
                            string warning = $"Mask plugin '{saved.maskPluginId}' is unavailable; cached mask was retained.";
                            if (options.strictPlugins) throw new InvalidOperationException(warning);
                            result.warnings.Add(warning);
                        }
                        else
                        {
                            await plugins.ExecuteLayerMaskAsync(plugin, store,
                                saved.maskPluginParameters?.Clone() ?? plugins.CreateParameters(plugin),
                                peers, ProgressSlice(progress, completedStructuralWork, structuralWork,
                                    completedProcedural, proceduralCount), token, false);
                            RefreshReplacedDestinations(peers, result.created);
                        }
                    }
                    if (!string.IsNullOrEmpty(saved.pluginId) || !string.IsNullOrEmpty(saved.maskPluginId))
                        completedProcedural++;
                    foreach (TextureSet set in peers.Keys) set.BindPreviewTextures();
                }
                progress?.Report(1f);
                return result;
            }
            catch
            {
                Rollback(result.created);
                throw;
            }
        }

        private static TexturePaintLayer RestoreLayer(TextureSet set, TexturePaintDocumentLayer saved,
            TexturePaintMaterialPresetApplyOptions options, List<string> warnings)
        {
            TexturePaintLayer layer = NewLayer(saved.name, saved.kind);
            layer.visible = saved.visible;
            layer.opacity = saved.opacity;
            layer.blendMode = saved.blendMode;
            layer.effects = saved.effects?.Clone() ?? new TexturePaintLayerEffects();
            layer.fillChannel = saved.fillChannel;
            layer.fillColor = saved.fillColor;
            layer.fillSettings = saved.fillSettings?.Clone();
            layer.paintSettings = saved.paintSettings?.Clone();
            layer.spline = saved.kind == TexturePaintLayerKind.Spline && saved.spline != null
                ? JsonUtility.FromJson<TexturePaintSpline>(JsonUtility.ToJson(saved.spline)) : null;
            layer.splineSettings = saved.kind == TexturePaintLayerKind.Spline
                ? saved.splineSettings?.Clone() : null;
            layer.pluginId = saved.pluginId;
            layer.pluginVersion = saved.pluginVersion;
            layer.pluginParametersJson = saved.pluginParametersJson;
            layer.pluginParameters = saved.pluginParameters?.Clone() ?? new TexturePaintPluginParameterSet();
            layer.pluginStale = !string.IsNullOrEmpty(saved.pluginId);
            layer.pluginLastError = null;
            layer.proceduralGroupKey = string.IsNullOrEmpty(saved.pluginId) ? null :
                "texture-paint-preset:" + Guid.NewGuid().ToString("N");
            layer.NormalizeKindPayload();
            if (saved.strokes != null)
                for (int i = 0; i < saved.strokes.Count; i++)
                    if (saved.strokes[i] != null)
                        layer.strokes.Add(JsonUtility.FromJson<TexturePaintStrokeRecord>(
                            JsonUtility.ToJson(saved.strokes[i])));

            if (saved.channels != null)
                for (int i = 0; i < saved.channels.Count; i++)
                {
                    TexturePaintDocumentLayerChannel savedChannel = saved.channels[i];
                    if (savedChannel == null) continue;
                    TextureChannelTarget baseChannel = set.GetChannel(savedChannel.channel);
                    if (baseChannel == null)
                    {
                        if (options.strictChannels)
                            throw new InvalidOperationException($"{set.Name} does not support required preset channel {savedChannel.channel}.");
                        warnings.Add($"{set.Name}: skipped unsupported {savedChannel.channel} channel on '{saved.name}'.");
                        continue;
                    }
                    EditableTextureTarget target = new EditableTextureTarget(
                        layer.name + " " + savedChannel.channel, baseChannel.editable.Width,
                        baseChannel.editable.Height, baseChannel.format, null, Color.clear);
                    RestorePixels(savedChannel.pixels, target);
                    layer.channels[savedChannel.channel] = target;
                    TexturePaintLayerChannelSettings settings = savedChannel.settings?.Clone() ??
                        new TexturePaintLayerChannelSettings { channel = savedChannel.channel };
                    settings.channel = savedChannel.channel;
                    settings.sourceSettings = savedChannel.GetSourceSettings();
                    layer.channelSettings[savedChannel.channel] = settings;
                }

            if (saved.hasMask)
            {
                set.layers.Add(layer);
                TexturePaintLayerMask mask = set.AddLayerMask(layer, saved.maskBaseValue);
                set.layers.Remove(layer);
                if (mask != null)
                {
                    mask.effects = saved.maskEffects?.Clone() ?? new TexturePaintLayerMaskEffects();
                    mask.sourceSettings = saved.maskSourceSettings?.Clone() ??
                        TexturePaintLayerMask.DefaultSourceSettings();
                    mask.sourceChannel = saved.maskSourceChannel;
                    mask.pluginId = saved.maskPluginId;
                    mask.pluginVersion = saved.maskPluginVersion;
                    mask.pluginParametersJson = saved.maskPluginParametersJson;
                    mask.pluginParameters = saved.maskPluginParameters?.Clone() ??
                        new TexturePaintPluginParameterSet();
                    mask.pluginStale = !string.IsNullOrEmpty(saved.maskPluginId);
                    mask.pluginLastError = null;
                    mask.NormalizePaintSource();
                    RestorePixels(saved.maskPixels, mask.target);
                }
            }
            return layer;
        }

        private static TexturePaintLayer NewLayer(string name, TexturePaintLayerKind kind) =>
            new TexturePaintLayer
            {
                id = Guid.NewGuid().ToString("N"),
                name = string.IsNullOrWhiteSpace(name) ? "Material Preset Layer" : name,
                kind = kind,
                visible = true
            };

        private static TexturePaintMaterialPresetCreatedLayer Location(TextureSet set,
            TexturePaintLayer layer) => new TexturePaintMaterialPresetCreatedLayer
            {
                set = set,
                layer = layer,
                index = set.layers.IndexOf(layer)
            };

        private static TexturePaintDocumentLayer CaptureLayer(TexturePaintLayer layer,
            bool includeCachedPluginOutput)
        {
            layer.layerMask?.NormalizePaintSource();
            var saved = new TexturePaintDocumentLayer
            {
                name = layer.name,
                kind = layer.kind,
                visible = layer.visible,
                opacity = layer.opacity,
                blendMode = layer.blendMode,
                effects = layer.effects?.Clone() ?? new TexturePaintLayerEffects(),
                fillChannel = layer.fillChannel,
                fillColor = layer.fillColor,
                fillSettings = layer.fillSettings?.Clone(),
                paintSettings = layer.paintSettings?.Clone(),
                spline = layer.IsSplineLayer && layer.spline != null
                    ? JsonUtility.FromJson<TexturePaintSpline>(JsonUtility.ToJson(layer.spline)) : null,
                splineSettings = layer.IsSplineLayer ? layer.splineSettings?.Clone() : null,
                pluginId = layer.pluginId,
                pluginVersion = layer.pluginVersion,
                pluginParametersJson = layer.pluginParametersJson,
                pluginParameters = layer.pluginParameters?.Clone() ?? new TexturePaintPluginParameterSet(),
                pluginStale = layer.pluginStale,
                pluginLastError = layer.pluginLastError,
                hasMask = layer.layerMask?.target?.Front != null,
                maskBaseValue = layer.layerMask?.baseValue ?? 1f,
                maskEffects = layer.layerMask?.effects?.Clone() ?? new TexturePaintLayerMaskEffects(),
                maskSourceSettings = layer.layerMask?.sourceSettings?.Clone() ??
                    TexturePaintLayerMask.DefaultSourceSettings(),
                maskSourceChannel = layer.layerMask?.sourceChannel ?? TexturePaintChannel.Albedo,
                maskPluginId = layer.layerMask?.pluginId,
                maskPluginVersion = layer.layerMask?.pluginVersion,
                maskPluginParametersJson = layer.layerMask?.pluginParametersJson,
                maskPluginParameters = layer.layerMask?.pluginParameters?.Clone() ??
                    new TexturePaintPluginParameterSet(),
                maskPluginStale = layer.layerMask?.pluginStale ?? true,
                maskPluginLastError = layer.layerMask?.pluginLastError
            };
            for (int i = 0; i < layer.strokes.Count; i++)
                saved.strokes.Add(JsonUtility.FromJson<TexturePaintStrokeRecord>(
                    JsonUtility.ToJson(layer.strokes[i])));
            foreach (KeyValuePair<TexturePaintChannel, EditableTextureTarget> pair in layer.channels)
            {
                TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(pair.Key, false) ??
                    new TexturePaintLayerChannelSettings
                    {
                        channel = pair.Key,
                        opacity = 1f,
                        blendMode = layer.blendMode
                    };
                var channel = new TexturePaintDocumentLayerChannel
                {
                    channel = pair.Key,
                    settings = settings.Clone(),
                    pixels = layer.kind == TexturePaintLayerKind.Plugin && !includeCachedPluginOutput
                        ? new TexturePaintPixelData()
                        : CapturePixels(pair.Value.Front, pair.Value.Front.sRGB)
                };
                channel.SetSourceSettings(settings.sourceSettings);
                saved.channels.Add(channel);
            }
            if (layer.layerMask?.target?.Front != null)
                saved.maskPixels = CapturePixels(layer.layerMask.target.Front, false);
            return saved;
        }

        private static TexturePaintPixelData CapturePixels(RenderTexture source, bool sRGB)
        {
            if (source == null) return new TexturePaintPixelData();
            TextureFormat format = ToTextureFormat(source.format);
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                RenderTexture.active = source;
                readback = new Texture2D(source.width, source.height, format, false, !sRGB);
                readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                readback.Apply(false, false);
                byte[] raw = readback.GetRawTextureData<byte>().ToArray();
                byte[] compressed = Compress(raw);
                return new TexturePaintPixelData
                {
                    width = source.width,
                    height = source.height,
                    textureFormat = format,
                    linear = !sRGB,
                    uncompressedByteCount = raw.Length,
                    checksum = TexturePaintDocumentBlobUtility.ComputeChecksum(compressed),
                    compressedBytes = compressed
                };
            }
            finally
            {
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                RenderTexture.active = previous;
            }
        }

        private static void RestorePixels(TexturePaintPixelData pixels,
            EditableTextureTarget destination)
        {
            if (pixels == null || !pixels.HasData || destination == null) return;
            byte[] compressed = pixels.GetCompressedBytes();
            if (!TexturePaintDocumentBlobUtility.VerifyChecksum(compressed, pixels.checksum))
                throw new InvalidDataException("Material preset pixel data failed its checksum.");
            byte[] raw = Decompress(compressed, pixels.uncompressedByteCount);
            Texture2D texture = new Texture2D(pixels.width, pixels.height, pixels.textureFormat,
                false, pixels.linear);
            try
            {
                texture.LoadRawTextureData(raw);
                texture.Apply(false, false);
                destination.Reset(texture, Color.clear);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static TexturePaintPresetPortability PortabilityOf(TexturePaintLayer layer)
        {
            TexturePaintPresetPortability result = TexturePaintPresetPortability.Portable;
            if (layer.kind == TexturePaintLayerKind.Paint || layer.kind == TexturePaintLayerKind.Spline ||
                layer.layerMask?.target?.Front != null)
                result |= TexturePaintPresetPortability.UVDependent;
            if (!string.IsNullOrEmpty(layer.pluginId) || !string.IsNullOrEmpty(layer.layerMask?.pluginId))
                result |= TexturePaintPresetPortability.RequiresPlugin;
            return result;
        }

        private static void AddPluginRequirement(TexturePaintMaterialPreset preset,
            PluginHost plugins, string pluginId, string savedVersion,
            TexturePaintPluginTarget target,
            HashSet<string> pluginIds)
        {
            if (string.IsNullOrEmpty(pluginId)) return;
            string key = pluginId + ":" + target;
            if (!pluginIds.Add(key)) return;
            ITexturePaintCommandExtensionV2 plugin = plugins?.FindCommand(pluginId);
            TexturePaintPluginDescriptor descriptor = plugin?.Descriptor;
            preset.plugins.Add(new TexturePaintMaterialPresetPlugin
            {
                pluginId = pluginId,
                pluginVersion = descriptor?.pluginVersion ?? savedVersion,
                apiVersion = descriptor?.apiVersion ?? 0,
                declaredChannels = descriptor?.declaredChannels ?? TexturePaintChannelMask.None,
                readChannels = descriptor?.readChannels ?? TexturePaintChannelMask.None,
                requiredMeshMaps = descriptor?.ResolvedMeshMaps ?? TexturePaintMeshMapMask.None,
                targets = target
            });
            if (descriptor?.ResolvedMeshMaps != TexturePaintMeshMapMask.None)
                preset.portability |= TexturePaintPresetPortability.MeshDependent;
        }

        private static void ValidateSource(string layerName, TexturePaintFillSettings source,
            TexturePaintMaterialPresetCompatibility report)
        {
            if (source == null) return;
            if (source.source == TexturePaintBrushSource.Texture && source.sourceTexture == null &&
                source.sourceSprite == null)
                report.issues.Add(Warning($"'{layerName}' references a missing texture or sprite."));
            else if (source.source == TexturePaintBrushSource.Overlay && source.sourceOverlay == null)
                report.issues.Add(Warning($"'{layerName}' references a missing OverlayDataAsset."));
        }

        private static void ValidateSource(string layerName,
            TexturePaintChannelSourceSettings source,
            TexturePaintMaterialPresetCompatibility report)
        {
            if (source == null) return;
            if (source.source == TexturePaintBrushSource.Texture && source.sourceTexture == null &&
                source.sourceSprite == null)
                report.issues.Add(Warning($"'{layerName}' references a missing channel texture or sprite."));
            else if (source.source == TexturePaintBrushSource.Overlay && source.sourceOverlay == null)
                report.issues.Add(Warning($"'{layerName}' references a missing channel OverlayDataAsset."));
        }

        private static IProgress<float> ProgressSlice(IProgress<float> progress,
            int completedStructural, int structuralCount, int completedProcedural, int proceduralCount)
        {
            if (progress == null) return null;
            float structural = structuralCount <= 0 ? 0.5f : 0.5f * completedStructural / structuralCount;
            float span = proceduralCount <= 0 ? 0.5f : 0.5f / proceduralCount;
            return new Progress<float>(value => progress.Report(Mathf.Clamp01(structural +
                span * (completedProcedural + Mathf.Clamp01(value)))));
        }

        private static void RefreshReplacedDestinations(
            Dictionary<TextureSet, TexturePaintLayer> destinations,
            List<TexturePaintMaterialPresetCreatedLayer> created)
        {
            if (destinations == null) return;
            foreach (TextureSet set in destinations.Keys.ToArray())
            {
                TexturePaintLayer previous = destinations[set];
                if (set == null || previous == null) continue;
                TexturePaintLayer replacement = null;
                for (int i = 0; i < set.layers.Count; i++)
                    if (string.Equals(set.layers[i]?.id, previous.id, StringComparison.Ordinal))
                    {
                        replacement = set.layers[i];
                        break;
                    }
                if (replacement == null || ReferenceEquals(replacement, previous)) continue;
                destinations[set] = replacement;
                for (int i = 0; i < created.Count; i++)
                    if (created[i]?.set == set && ReferenceEquals(created[i].layer, previous))
                    {
                        created[i].layer = replacement;
                        created[i].index = set.layers.IndexOf(replacement);
                        break;
                    }
            }
        }

        private static bool HasMissingFillSource(TexturePaintLayer layer)
        {
            if (layer?.kind != TexturePaintLayerKind.Fill) return false;
            foreach (TexturePaintChannel channel in layer.channels.Keys)
            {
                TexturePaintChannelSourceSettings source =
                    layer.GetChannelSettings(channel, false)?.sourceSettings;
                if (source == null) continue;
                if (source.source == TexturePaintBrushSource.Texture && source.sourceTexture == null &&
                    source.sourceSprite == null) return true;
                if (source.source == TexturePaintBrushSource.Overlay && source.sourceOverlay == null)
                    return true;
            }
            return false;
        }

        private static void RestoreLayerChannelPixels(TexturePaintDocumentLayer saved,
            TexturePaintLayer layer)
        {
            if (saved?.channels == null || layer == null) return;
            for (int i = 0; i < saved.channels.Count; i++)
            {
                TexturePaintDocumentLayerChannel channel = saved.channels[i];
                if (channel != null && layer.channels.TryGetValue(channel.channel,
                        out EditableTextureTarget target))
                    RestorePixels(channel.pixels, target);
            }
        }

        private static void Rollback(List<TexturePaintMaterialPresetCreatedLayer> created)
        {
            var changed = new HashSet<TextureSet>();
            for (int i = created.Count - 1; i >= 0; i--)
            {
                TexturePaintMaterialPresetCreatedLayer item = created[i];
                if (item?.set == null || item.layer == null) continue;
                if (item.set.layers.Remove(item.layer)) item.layer.Dispose();
                item.set.activeLayerIndex = Mathf.Clamp(item.set.activeLayerIndex, -1,
                    item.set.layers.Count - 1);
                changed.Add(item.set);
            }
            foreach (TextureSet set in changed) set.BindPreviewTextures();
            created.Clear();
        }

        private static TexturePaintMaterialPresetIssue Warning(string message) =>
            new TexturePaintMaterialPresetIssue
            {
                severity = TexturePaintMaterialPresetIssueSeverity.Warning,
                message = message
            };

        private static TexturePaintMaterialPresetIssue Error(string message) =>
            new TexturePaintMaterialPresetIssue
            {
                severity = TexturePaintMaterialPresetIssueSeverity.Error,
                message = message
            };

        private static byte[] Compress(byte[] raw)
        {
            using MemoryStream output = new MemoryStream();
            using (DeflateStream stream = new DeflateStream(output,
                       System.IO.Compression.CompressionLevel.Optimal, true))
                stream.Write(raw, 0, raw.Length);
            return output.ToArray();
        }

        private static byte[] Decompress(byte[] compressed, int expectedLength)
        {
            using MemoryStream input = new MemoryStream(compressed);
            using DeflateStream stream = new DeflateStream(input, CompressionMode.Decompress);
            using MemoryStream output = expectedLength > 0
                ? new MemoryStream(expectedLength) : new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }

        private static TextureFormat ToTextureFormat(RenderTextureFormat format)
        {
            switch (format)
            {
                case RenderTextureFormat.ARGBHalf: return TextureFormat.RGBAHalf;
                case RenderTextureFormat.ARGBFloat: return TextureFormat.RGBAFloat;
                case RenderTextureFormat.RHalf: return TextureFormat.RHalf;
                case RenderTextureFormat.RFloat: return TextureFormat.RFloat;
                default: return TextureFormat.RGBA32;
            }
        }
    }
}
