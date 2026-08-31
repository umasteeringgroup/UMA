using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UMA.TexturePaint
{
    internal sealed class TexturePaintPluginCommit : IDisposable
    {
        internal sealed class LayerBinding
        {
            public TextureSet set;
            public TexturePaintLayer layer;
            public int index;
        }

        internal sealed class LayerReplacement
        {
            public TextureSet set;
            public TexturePaintLayer before;
            public TexturePaintLayer after;
            public int index;
        }

        private readonly List<LayerBinding> layers;
        private readonly List<LayerReplacement> replacements;
        private bool applied = true;
        public long dirtyPixels { get; }
        public int commandCount { get; }
        public bool hasChanges => layers.Count > 0 || replacements.Count > 0;

        internal TexturePaintPluginCommit(List<LayerBinding> layers, long dirtyPixels, int commandCount)
            : this(layers, new List<LayerReplacement>(), dirtyPixels, commandCount) { }

        internal TexturePaintPluginCommit(List<LayerBinding> layers,
            List<LayerReplacement> replacements, long dirtyPixels, int commandCount)
        {
            this.layers = layers ?? new List<LayerBinding>();
            this.replacements = replacements ?? new List<LayerReplacement>();
            this.dirtyPixels = dirtyPixels;
            this.commandCount = commandCount;
        }

        public void Undo()
        {
            if (!applied) return;
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                LayerBinding binding = layers[i];
                int index = binding.set.layers.IndexOf(binding.layer);
                if (index >= 0) binding.set.layers.RemoveAt(index);
                binding.set.activeLayerIndex = Mathf.Clamp(binding.set.activeLayerIndex, -1, binding.set.layers.Count - 1);
                binding.set.BindPreviewTextures();
            }
            for (int i = replacements.Count - 1; i >= 0; i--)
                Swap(replacements[i], replacements[i].after, replacements[i].before);
            applied = false;
        }

        public void Redo()
        {
            if (applied) return;
            for (int i = 0; i < layers.Count; i++)
            {
                LayerBinding binding = layers[i];
                int index = Mathf.Clamp(binding.index, 0, binding.set.layers.Count);
                binding.set.layers.Insert(index, binding.layer);
                binding.set.activeLayerIndex = index;
                binding.set.BindPreviewTextures();
            }
            for (int i = 0; i < replacements.Count; i++)
                Swap(replacements[i], replacements[i].before, replacements[i].after);
            applied = true;
        }

        public void Dispose()
        {
            if (!applied)
                for (int i = 0; i < layers.Count; i++) layers[i].layer?.Dispose();
            layers.Clear();
            for (int i = 0; i < replacements.Count; i++)
                (applied ? replacements[i].before : replacements[i].after)?.Dispose();
            replacements.Clear();
        }

        private static void Swap(LayerReplacement replacement, TexturePaintLayer expected,
            TexturePaintLayer value)
        {
            if (replacement?.set == null || value == null) return;
            int index = replacement.set.layers.IndexOf(expected);
            if (index < 0 && (uint)replacement.index < (uint)replacement.set.layers.Count)
            {
                TexturePaintLayer candidate = replacement.set.layers[replacement.index];
                if (candidate != null && (candidate.id == expected?.id || candidate.id == value.id))
                    index = replacement.index;
            }
            if ((uint)index >= (uint)replacement.set.layers.Count) return;
            replacement.set.layers[index] = value;
            replacement.set.activeLayerIndex = index;
            replacement.set.RecomposeAll();
            replacement.set.BindPreviewTextures();
        }
    }

    internal static class TexturePaintPluginTransactionExecutor
    {
        public static TexturePaintReadContextV2 Capture(TextureStore store, TexturePaintPluginDescriptor descriptor,
            TexturePaintPluginParameterSet parameters, System.Threading.CancellationToken token,
            IProgress<float> progress, long memoryBudgetBytes,
            IReadOnlyDictionary<TextureSet, TexturePaintLayer> inputBoundaries = null,
            TexturePaintChannelMask? readChannelsOverride = null,
            bool captureLayerMasks = false)
        {
            var images = new Dictionary<string, TexturePaintReadOnlyImage>(StringComparer.Ordinal);
            var channelInfo = new Dictionary<string, TexturePaintReadOnlyChannelInfo>(StringComparer.Ordinal);
            var meshMapImages = new Dictionary<string, TexturePaintReadOnlyMeshMap>(StringComparer.Ordinal);
            var parameterTextures = new Dictionary<string, TexturePaintReadOnlyParameterTexture>(StringComparer.Ordinal);
            var masks = new Dictionary<string, TexturePaintReadOnlyMask>(StringComparer.Ordinal);
            var surfaceIds = new List<string>();
            TexturePaintChannel[] channels = (TexturePaintChannel[])Enum.GetValues(typeof(TexturePaintChannel));
            TexturePaintMeshMap[] meshMaps = (TexturePaintMeshMap[])Enum.GetValues(typeof(TexturePaintMeshMap));
            int parameterTextureCount = CountParameterTextures(descriptor, parameters);
            int meshMapCount = 0;
            for (int i = 0; i < meshMaps.Length; i++) if (descriptor.Requires(meshMaps[i])) meshMapCount++;
            var captureSets = new List<TextureSet>();
            if (store != null)
                for (int i = 0; i < store.Sets.Count; i++)
                {
                    TextureSet candidate = store.Sets[i];
                    if (inputBoundaries == null || inputBoundaries.ContainsKey(candidate))
                        captureSets.Add(candidate);
                }
            int setCount = captureSets.Count;
            int total = Mathf.Max(1, setCount * (channels.Length + meshMapCount +
                (captureLayerMasks ? 1 : 0)) + parameterTextureCount);
            int completed = 0;
            long capturedBytes = 0L;
            for (int setIndex = 0; setIndex < setCount; setIndex++)
            {
                TextureSet set = captureSets[setIndex];
                string surfaceId = set.persistentId ?? set.surface?.index.ToString() ?? setIndex.ToString();
                surfaceIds.Add(surfaceId);
                if (captureLayerMasks)
                {
                    token.ThrowIfCancellationRequested();
                    if (inputBoundaries == null || !inputBoundaries.TryGetValue(set,
                            out TexturePaintLayer maskLayer) || maskLayer?.layerMask?.target?.Front == null)
                        throw new InvalidOperationException(
                            "Layer-mask plugin execution requires a mask on every logical destination layer.");
                    Color[] maskPixels = ReadScaled(maskLayer.layerMask.target.Front,
                        descriptor.channelSnapshotMaximumResolution,
                        out int maskWidth, out int maskHeight);
                    AddCapturedBytes(ref capturedBytes, maskPixels, memoryBudgetBytes);
                    masks[surfaceId] = new TexturePaintReadOnlyMask(surfaceId, maskWidth,
                        maskHeight, maskPixels);
                    completed++;
                    progress?.Report(completed / (float)total * 0.25f);
                }
                for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    TexturePaintChannel channel = channels[channelIndex];
                    TextureChannelTarget target = set.GetChannel(channel);
                    RenderTexture nativeTexture = target?.Texture;
                    if (target != null && nativeTexture != null)
                    {
                        channelInfo[TexturePaintReadContextV2.Key(surfaceId, channel)] =
                            new TexturePaintReadOnlyChannelInfo(surfaceId, channel, nativeTexture.width,
                                nativeTexture.height, target.sRGB);
                    }
                    TexturePaintChannelMask reads = readChannelsOverride ??
                        descriptor.ResolvedReadChannels;
                    if ((reads & TexturePaintExportTemplate.ToMask(channel)) != 0)
                    {
                        RenderTexture source = set.GetVisibleTexture(channel);
                        if (target != null && source != null)
                        {
                            RenderTexture belowLayer = null;
                            try
                            {
                                if (inputBoundaries != null && inputBoundaries.TryGetValue(set,
                                        out TexturePaintLayer boundary) && boundary != null)
                                {
                                    RenderTextureDescriptor temporaryDescriptor = source.descriptor;
                                    temporaryDescriptor.depthBufferBits = 0;
                                    temporaryDescriptor.msaaSamples = 1;
                                    temporaryDescriptor.enableRandomWrite = true;
                                    belowLayer = RenderTexture.GetTemporary(temporaryDescriptor);
                                    if (!set.CompositeBelowLayer(channel, boundary, belowLayer))
                                        Graphics.Blit(target.Texture, belowLayer);
                                    source = belowLayer;
                                }
                                Color[] pixels = ReadScaled(source,
                                    descriptor.channelSnapshotMaximumResolution,
                                    out int snapshotWidth, out int snapshotHeight);
                                AddCapturedBytes(ref capturedBytes, pixels, memoryBudgetBytes);
                                images[TexturePaintReadContextV2.Key(surfaceId, channel)] =
                                    new TexturePaintReadOnlyImage(surfaceId, channel, snapshotWidth,
                                        snapshotHeight, target.sRGB, pixels);
                            }
                            finally
                            {
                                if (belowLayer != null) RenderTexture.ReleaseTemporary(belowLayer);
                            }
                        }
                    }
                    completed++;
                    progress?.Report(completed / (float)total * 0.25f);
                }

                if (meshMapCount > 0)
                {
                    token.ThrowIfCancellationRequested();
                    ProceduralMeshMaps available = set.GetProceduralMeshMaps(1024,
                        new TexturePaintOperationContext(token));
                    for (int mapIndex = 0; mapIndex < meshMaps.Length; mapIndex++)
                    {
                        TexturePaintMeshMap map = meshMaps[mapIndex];
                        if (!descriptor.Requires(map)) continue;
                        token.ThrowIfCancellationRequested();
                        Texture2D texture = ResolveMeshMap(available, map);
                        if (texture != null)
                        {
                            Color[] pixels = texture.GetPixels();
                            AddCapturedBytes(ref capturedBytes, pixels, memoryBudgetBytes);
                            meshMapImages[TexturePaintReadContextV2.MeshKey(surfaceId, map)] =
                                new TexturePaintReadOnlyMeshMap(surfaceId, map, texture.width, texture.height, pixels);
                        }
                        completed++;
                        progress?.Report(completed / (float)total * 0.25f);
                    }
                }
            }

            if (descriptor?.parameters != null)
            {
                for (int i = 0; i < descriptor.parameters.Count; i++)
                {
                    TexturePaintPluginParameterDefinition definition = descriptor.parameters[i];
                    if (definition == null || (definition.type != TexturePaintPluginParameterType.Texture &&
                            definition.type != TexturePaintPluginParameterType.Sprite)) continue;
                    Texture2D texture = definition.type == TexturePaintPluginParameterType.Sprite
                        ? parameters?.Sprite(definition.id)?.texture
                        : parameters?.Texture(definition.id);
                    if (texture == null) continue;
                    token.ThrowIfCancellationRequested();
                    Color[] pixels;
                    int parameterWidth, parameterHeight;
                    if (definition.type == TexturePaintPluginParameterType.Sprite)
                        pixels = ReadSprite(parameters.Sprite(definition.id), out parameterWidth,
                            out parameterHeight);
                    else
                    {
                        pixels = Read(texture); parameterWidth = texture.width;
                        parameterHeight = texture.height;
                    }
                    AddCapturedBytes(ref capturedBytes, pixels, memoryBudgetBytes);
                    parameterTextures[definition.id] = new TexturePaintReadOnlyParameterTexture(
                        definition.id, parameterWidth, parameterHeight, false, pixels);
                    completed++;
                    progress?.Report(completed / (float)total * 0.25f);
                }
            }

            return new TexturePaintReadContextV2(images, channelInfo, meshMapImages,
                parameterTextures, surfaceIds, masks);
        }

        private static int CountParameterTextures(TexturePaintPluginDescriptor descriptor,
            TexturePaintPluginParameterSet parameters)
        {
            if (descriptor?.parameters == null || parameters == null) return 0;
            int count = 0;
            for (int i = 0; i < descriptor.parameters.Count; i++)
            {
                TexturePaintPluginParameterDefinition definition = descriptor.parameters[i];
                if (definition?.type == TexturePaintPluginParameterType.Texture &&
                    parameters.Texture(definition.id) != null) count++;
                else if (definition?.type == TexturePaintPluginParameterType.Sprite &&
                    parameters.Sprite(definition.id) != null) count++;
            }
            return count;
        }

        private static void AddCapturedBytes(ref long capturedBytes, Color[] pixels, long memoryBudgetBytes)
        {
            capturedBytes += (pixels?.LongLength ?? 0L) * 16L;
            if (capturedBytes > memoryBudgetBytes)
                throw new InvalidOperationException("Plugin snapshot memory budget exceeded.");
        }

        private static Texture2D ResolveMeshMap(ProceduralMeshMaps maps, TexturePaintMeshMap map)
        {
            if (maps == null) return null;
            return map switch
            {
                TexturePaintMeshMap.WorldPosition => maps.position,
                TexturePaintMeshMap.WorldNormal => maps.worldNormal,
                TexturePaintMeshMap.SignedCurvature => maps.curvature,
                TexturePaintMeshMap.AmbientOcclusion => maps.ambientOcclusion,
                TexturePaintMeshMap.Thickness => maps.thickness,
                TexturePaintMeshMap.SurfaceId => maps.id,
                _ => null
            };
        }

        public static TexturePaintPluginCommit Commit(TextureStore store,
            TexturePaintPluginDescriptor descriptor, IReadOnlyList<TexturePaintPluginTileCommand> commands,
            System.Threading.CancellationToken token, IProgress<float> progress,
            TexturePaintPluginParameterSet parameters = null,
            TexturePaintLogicalLayerController logicalLayers = null)
        {
            if (commands == null || commands.Count == 0) return new TexturePaintPluginCommit(new List<TexturePaintPluginCommit.LayerBinding>(), 0L, 0);
            var bindings = new List<TexturePaintPluginCommit.LayerBinding>();
            var layers = new Dictionary<TextureSet, TexturePaintLayer>();
            var geometryMasks = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            long dirtyPixels = 0L;
            try
            {
                ValidateAll(store, descriptor, commands);
                for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    TexturePaintPluginTileCommand command = commands[commandIndex];
                    TextureSet set = FindSet(store, command.surfaceId);
                    TextureChannelTarget baseTarget = set.GetChannel(command.channel);
                    if (!layers.TryGetValue(set, out TexturePaintLayer layer))
                    {
                        layer = set.AddLayer("Plugin · " + descriptor.displayName);
                        layer.kind = TexturePaintLayerKind.Plugin;
                        layer.pluginId = descriptor.id;
                        layer.pluginVersion = descriptor.pluginVersion;
                        layer.pluginParameters = parameters?.Clone() ?? new TexturePaintPluginParameterSet();
                        layer.pluginParametersJson = JsonUtility.ToJson(layer.pluginParameters);
                        layer.pluginStale = false;
                        layers.Add(set, layer);
                        bindings.Add(new TexturePaintPluginCommit.LayerBinding { set = set, layer = layer, index = set.layers.IndexOf(layer) });
                    }
                    if (!layer.channels.TryGetValue(command.channel, out EditableTextureTarget target))
                    {
                        target = new EditableTextureTarget(layer.name + " " + command.channel, baseTarget.Texture.width,
                            baseTarget.Texture.height, baseTarget.format, null, Color.clear);
                        layer.channels.Add(command.channel, target);
                        layer.GetChannelSettings(command.channel);
                    }
                    string maskKey = set.persistentId + "|" + target.Width + "x" + target.Height;
                    if (!geometryMasks.TryGetValue(maskKey, out Texture2D geometryMask))
                    {
                        geometryMask = TexturePaintGeometryMask.Build(set.surface, target.Width,
                            target.Height, null, -1, null);
                        geometryMasks.Add(maskKey, geometryMask);
                    }
                    Apply(target, baseTarget, command, geometryMask, set.channelPackShader);
                    set.CompositeChannel(command.channel, command.rect);
                    set.BindPreviewTextures(false, command.rect);
                    dirtyPixels += (long)command.rect.width * command.rect.height;
                    progress?.Report(0.25f + 0.75f * ((commandIndex + 1f) / commands.Count));
                }
                LinkPluginLayers(bindings, logicalLayers);
                return new TexturePaintPluginCommit(bindings, dirtyPixels, commands.Count);
            }
            catch
            {
                for (int i = bindings.Count - 1; i >= 0; i--)
                {
                    TexturePaintPluginCommit.LayerBinding binding = bindings[i];
                    binding.set.layers.Remove(binding.layer); binding.layer.Dispose(); binding.set.BindPreviewTextures();
                }
                throw;
            }
            finally
            {
                foreach (Texture2D mask in geometryMasks.Values) Destroy(mask);
            }
        }

        public static TexturePaintPluginCommit CommitGpuGenerator(TextureStore store,
            TexturePaintPluginDescriptor descriptor, string kernelName, ComputeShader shader,
            System.Threading.CancellationToken token, IProgress<float> progress,
            TexturePaintPluginParameterSet parameters = null,
            TexturePaintLogicalLayerController logicalLayers = null)
        {
            if (store == null || descriptor == null || shader == null ||
                string.IsNullOrWhiteSpace(kernelName) || !SystemInfo.supportsComputeShaders ||
                !shader.HasKernel(kernelName))
                return new TexturePaintPluginCommit(
                    new List<TexturePaintPluginCommit.LayerBinding>(), 0L, 0);
            int kernel = shader.FindKernel(kernelName);
            if (!shader.IsSupported(kernel))
                return new TexturePaintPluginCommit(
                    new List<TexturePaintPluginCommit.LayerBinding>(), 0L, 0);

            var bindings = new List<TexturePaintPluginCommit.LayerBinding>();
            long dirtyPixels = 0L;
            int dispatchCount = 0;
            try
            {
                for (int setIndex = 0; setIndex < store.Sets.Count; setIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    TextureSet set = store.Sets[setIndex];
                    List<TexturePaintChannel> channels = FindGpuOutputChannels(set, descriptor);
                    if (channels.Count == 0) continue;
                    TexturePaintLayer layer = set.AddLayer("Plugin · " + descriptor.displayName);
                    layer.kind = TexturePaintLayerKind.Plugin;
                    ConfigurePluginLayer(layer, descriptor, parameters);
                    bindings.Add(new TexturePaintPluginCommit.LayerBinding
                    {
                        set = set, layer = layer, index = set.layers.IndexOf(layer)
                    });
                    DispatchGpuGenerator(set, layer, channels, descriptor, kernel, shader,
                        parameters, token, ref dirtyPixels, ref dispatchCount);
                    set.RecomposeAll();
                    set.BindPreviewTextures();
                    progress?.Report((setIndex + 1f) / Mathf.Max(1, store.Sets.Count));
                }
                LinkPluginLayers(bindings, logicalLayers);
                progress?.Report(1f);
                return new TexturePaintPluginCommit(bindings, dirtyPixels, dispatchCount);
            }
            catch
            {
                for (int i = bindings.Count - 1; i >= 0; i--)
                {
                    TexturePaintPluginCommit.LayerBinding binding = bindings[i];
                    binding.set.layers.Remove(binding.layer);
                    binding.layer.Dispose();
                    binding.set.RecomposeAll();
                    binding.set.BindPreviewTextures();
                }
                throw;
            }
        }

        public static TexturePaintPluginCommit CommitGpuGeneratorIntoPluginLayers(
            TextureStore store, TexturePaintPluginDescriptor descriptor, string kernelName,
            ComputeShader shader, IReadOnlyDictionary<TextureSet, TexturePaintLayer> destinations,
            System.Threading.CancellationToken token, IProgress<float> progress,
            TexturePaintPluginParameterSet parameters)
        {
            if (destinations == null || destinations.Count == 0)
                throw new InvalidOperationException(
                    "A GPU Plugin layer regeneration requires at least one destination layer.");
            if (shader == null || string.IsNullOrWhiteSpace(kernelName) ||
                !SystemInfo.supportsComputeShaders || !shader.HasKernel(kernelName))
                throw new InvalidOperationException("The requested GPU generator kernel is unavailable.");
            int kernel = shader.FindKernel(kernelName);
            if (!shader.IsSupported(kernel))
                throw new InvalidOperationException("The requested GPU generator kernel is unsupported.");

            var replacements = new List<TexturePaintPluginCommit.LayerReplacement>();
            long dirtyPixels = 0L;
            int dispatchCount = 0;
            int swapped = 0;
            try
            {
                foreach (KeyValuePair<TextureSet, TexturePaintLayer> pair in destinations)
                {
                    token.ThrowIfCancellationRequested();
                    TextureSet set = pair.Key;
                    TexturePaintLayer before = pair.Value;
                    int index = set?.layers.IndexOf(before) ?? -1;
                    if (set == null || before == null ||
                        before.kind != TexturePaintLayerKind.Plugin || index < 0)
                        throw new InvalidOperationException("Plugin layer destination is missing or invalid.");
                    TexturePaintLayer after = set.CloneLayer(before, before.name, true);
                    ClearChannels(after);
                    after.kind = TexturePaintLayerKind.Plugin;
                    ConfigurePluginLayer(after, descriptor, parameters);
                    replacements.Add(new TexturePaintPluginCommit.LayerReplacement
                    {
                        set = set, before = before, after = after, index = index
                    });
                }

                for (int i = 0; i < replacements.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    TexturePaintPluginCommit.LayerReplacement replacement = replacements[i];
                    List<TexturePaintChannel> channels = FindGpuOutputChannels(replacement.set,
                        descriptor);
                    DispatchGpuGenerator(replacement.set, replacement.after, channels, descriptor,
                        kernel, shader, parameters, token, ref dirtyPixels, ref dispatchCount);
                    replacement.set.layers[replacement.index] = replacement.after;
                    swapped++;
                    replacement.set.activeLayerIndex = replacement.index;
                    replacement.set.RecomposeAll();
                    replacement.set.BindPreviewTextures();
                    progress?.Report((i + 1f) / Mathf.Max(1, replacements.Count));
                }
                progress?.Report(1f);
                return new TexturePaintPluginCommit(
                    new List<TexturePaintPluginCommit.LayerBinding>(), replacements,
                    dirtyPixels, dispatchCount);
            }
            catch
            {
                for (int i = swapped - 1; i >= 0; i--)
                {
                    TexturePaintPluginCommit.LayerReplacement replacement = replacements[i];
                    replacement.set.layers[replacement.index] = replacement.before;
                    replacement.set.activeLayerIndex = replacement.index;
                    try { replacement.set.RecomposeAll(); replacement.set.BindPreviewTextures(); }
                    catch (Exception) { }
                }
                for (int i = 0; i < replacements.Count; i++) replacements[i].after?.Dispose();
                throw;
            }
        }

        private static void DispatchGpuGenerator(TextureSet set, TexturePaintLayer layer,
            IReadOnlyList<TexturePaintChannel> channels, TexturePaintPluginDescriptor descriptor,
            int kernel, ComputeShader shader, TexturePaintPluginParameterSet parameters,
            System.Threading.CancellationToken token, ref long dirtyPixels, ref int dispatchCount)
        {
            if (set == null || layer == null || channels == null || channels.Count == 0) return;
            ProceduralMeshMaps maps = set.GetProceduralMeshMaps(1024,
                new TexturePaintOperationContext(token));
            BindGpuGeneratorInputs(set, maps, descriptor, kernel, shader, parameters);
            for (int i = 0; i < channels.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                TexturePaintChannel channel = channels[i];
                TextureChannelTarget baseTarget = set.GetChannel(channel);
                if (baseTarget?.Texture == null) continue;
                var target = new EditableTextureTarget(layer.name + " " + channel,
                    baseTarget.Texture.width, baseTarget.Texture.height, baseTarget.format,
                    null, Color.clear);
                layer.channels.Add(channel, target);
                layer.GetChannelSettings(channel);
                shader.SetInts("_OutputSize", target.Width, target.Height);
                shader.SetInt("_OutputChannel", (int)channel);
                shader.SetTexture(kernel, "_Output", target.Front);
                shader.Dispatch(kernel, Mathf.CeilToInt(target.Width / 16f),
                    Mathf.CeilToInt(target.Height / 16f), 1);
                target.CopyFrontToBack(new RectInt(0, 0, target.Width, target.Height));
                dirtyPixels += (long)target.Width * target.Height;
                dispatchCount++;
            }
        }

        private static void BindGpuGeneratorInputs(TextureSet set, ProceduralMeshMaps maps,
            TexturePaintPluginDescriptor descriptor, int kernel, ComputeShader shader,
            TexturePaintPluginParameterSet parameters)
        {
            Bind("_MeshWorldPosition", maps?.position, Texture2D.blackTexture);
            Bind("_MeshWorldNormal", maps?.worldNormal, Texture2D.grayTexture);
            Bind("_MeshSignedCurvature", maps?.curvature, Texture2D.grayTexture);
            Bind("_MeshAmbientOcclusion", maps?.ambientOcclusion, Texture2D.whiteTexture);
            Bind("_MeshThickness", maps?.thickness, Texture2D.blackTexture);
            Bind("_MeshSurfaceId", maps?.id, Texture2D.blackTexture);
            RenderTexture sourceNormal = set.GetVisibleTexture(TexturePaintChannel.Normal);
            RenderTexture sourceAo = set.GetVisibleTexture(TexturePaintChannel.AmbientOcclusion);
            shader.SetInt("_HasSourceNormal", sourceNormal != null ? 1 : 0);
            shader.SetInt("_HasSourceAO", sourceAo != null ? 1 : 0);
            shader.SetTexture(kernel, "_SourceNormal", sourceNormal != null
                ? sourceNormal : Texture2D.grayTexture);
            shader.SetTexture(kernel, "_SourceAO", sourceAo != null
                ? sourceAo : Texture2D.whiteTexture);
            shader.SetInt("_HasP_surfaceTexture", 0);
            shader.SetInt("_HasP_surfaceMask", 0);
            shader.SetTexture(kernel, "_P_surfaceTexture", Texture2D.whiteTexture);
            shader.SetTexture(kernel, "_P_surfaceMask", Texture2D.whiteTexture);

            IReadOnlyList<TexturePaintPluginParameterDefinition> definitions = descriptor.parameters;
            for (int i = 0; definitions != null && i < definitions.Count; i++)
            {
                TexturePaintPluginParameterDefinition definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.id) ||
                    definition.type == TexturePaintPluginParameterType.Header) continue;
                TexturePaintPluginParameterValue value = parameters?.Get(definition.id);
                string property = "_P_" + definition.id;
                switch (definition.type)
                {
                    case TexturePaintPluginParameterType.Float:
                        shader.SetFloat(property, value?.number ?? definition.defaultNumber);
                        break;
                    case TexturePaintPluginParameterType.Integer:
                    case TexturePaintPluginParameterType.Enum:
                        shader.SetInt(property,
                            Mathf.RoundToInt(value?.number ?? definition.defaultNumber));
                        break;
                    case TexturePaintPluginParameterType.Boolean:
                        shader.SetInt(property,
                            (value?.boolean ?? definition.defaultBoolean) ? 1 : 0);
                        break;
                    case TexturePaintPluginParameterType.Color:
                        shader.SetVector(property, value?.color ?? definition.defaultColor);
                        break;
                    case TexturePaintPluginParameterType.Texture:
                    {
                        Texture2D texture = value?.texture;
                        shader.SetInt("_HasP_" + definition.id, texture != null ? 1 : 0);
                        shader.SetTexture(kernel, property,
                            texture != null ? texture : Texture2D.whiteTexture);
                        break;
                    }
                }
            }
            return;

            void Bind(string name, Texture texture, Texture fallback)
            {
                shader.SetTexture(kernel, name, texture != null ? texture : fallback);
            }
        }

        private static List<TexturePaintChannel> FindGpuOutputChannels(TextureSet set,
            TexturePaintPluginDescriptor descriptor)
        {
            var result = new List<TexturePaintChannel>();
            TexturePaintChannel[] channels =
                (TexturePaintChannel[])Enum.GetValues(typeof(TexturePaintChannel));
            for (int i = 0; i < channels.Length; i++)
                if (descriptor.Declares(channels[i]) && set?.GetChannel(channels[i])?.Texture != null)
                    result.Add(channels[i]);
            return result;
        }

        private static void ConfigurePluginLayer(TexturePaintLayer layer,
            TexturePaintPluginDescriptor descriptor, TexturePaintPluginParameterSet parameters)
        {
            layer.pluginId = descriptor.id;
            layer.pluginVersion = descriptor.pluginVersion;
            layer.pluginParameters = parameters?.Clone() ?? new TexturePaintPluginParameterSet();
            layer.pluginParametersJson = JsonUtility.ToJson(layer.pluginParameters);
            layer.pluginStale = false;
            layer.pluginLastError = null;
        }

        public static TexturePaintPluginCommit CommitIntoPluginLayers(TextureStore store,
            TexturePaintPluginDescriptor descriptor,
            IReadOnlyList<TexturePaintPluginTileCommand> commands,
            IReadOnlyDictionary<TextureSet, TexturePaintLayer> destinations,
            System.Threading.CancellationToken token, IProgress<float> progress,
            TexturePaintPluginParameterSet parameters)
        {
            if (destinations == null || destinations.Count == 0)
                throw new InvalidOperationException("A Plugin layer regeneration requires at least one destination layer.");
            commands ??= Array.Empty<TexturePaintPluginTileCommand>();
            var replacements = new List<TexturePaintPluginCommit.LayerReplacement>();
            var bySet = new Dictionary<TextureSet, TexturePaintPluginCommit.LayerReplacement>();
            var geometryMasks = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            long dirtyPixels = 0L;
            int swapped = 0;
            try
            {
                ValidateAll(store, descriptor, commands);
                foreach (KeyValuePair<TextureSet, TexturePaintLayer> pair in destinations)
                {
                    TextureSet set = pair.Key;
                    TexturePaintLayer before = pair.Value;
                    int index = set?.layers.IndexOf(before) ?? -1;
                    if (set == null || before == null || before.kind != TexturePaintLayerKind.Plugin || index < 0)
                        throw new InvalidOperationException("Plugin layer destination is missing or invalid.");
                    TexturePaintLayer after = set.CloneLayer(before, before.name, true);
                    ClearChannels(after);
                    after.kind = TexturePaintLayerKind.Plugin;
                    after.pluginId = descriptor.id;
                    after.pluginVersion = descriptor.pluginVersion;
                    after.pluginParameters = parameters?.Clone() ?? new TexturePaintPluginParameterSet();
                    after.pluginParametersJson = JsonUtility.ToJson(after.pluginParameters);
                    after.pluginStale = false;
                    after.pluginLastError = null;
                    var replacement = new TexturePaintPluginCommit.LayerReplacement
                    {
                        set = set, before = before, after = after, index = index
                    };
                    replacements.Add(replacement);
                    bySet.Add(set, replacement);
                }

                for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    TexturePaintPluginTileCommand command = commands[commandIndex];
                    TextureSet set = FindSet(store, command.surfaceId);
                    if (!bySet.TryGetValue(set, out TexturePaintPluginCommit.LayerReplacement replacement))
                        throw new InvalidOperationException(
                            "Plugin submitted output for a surface outside this Plugin layer's logical target.");
                    TextureChannelTarget baseTarget = set.GetChannel(command.channel);
                    TexturePaintLayer layer = replacement.after;
                    if (!layer.channels.TryGetValue(command.channel, out EditableTextureTarget target))
                    {
                        target = new EditableTextureTarget(layer.name + " " + command.channel,
                            baseTarget.Texture.width, baseTarget.Texture.height, baseTarget.format,
                            null, Color.clear);
                        layer.channels.Add(command.channel, target);
                        layer.GetChannelSettings(command.channel);
                    }
                    string maskKey = set.persistentId + "|" + target.Width + "x" + target.Height;
                    if (!geometryMasks.TryGetValue(maskKey, out Texture2D geometryMask))
                    {
                        geometryMask = TexturePaintGeometryMask.Build(set.surface, target.Width,
                            target.Height, null, -1, null);
                        geometryMasks.Add(maskKey, geometryMask);
                    }
                    Apply(target, baseTarget, command, geometryMask, set.channelPackShader);
                    dirtyPixels += (long)command.rect.width * command.rect.height;
                    progress?.Report(0.25f + 0.7f * ((commandIndex + 1f) /
                        Mathf.Max(1, commands.Count)));
                }

                for (int i = 0; i < replacements.Count; i++)
                {
                    TexturePaintPluginCommit.LayerReplacement replacement = replacements[i];
                    replacement.set.layers[replacement.index] = replacement.after;
                    swapped++;
                    replacement.set.activeLayerIndex = replacement.index;
                    replacement.set.RecomposeAll();
                    replacement.set.BindPreviewTextures();
                }
                progress?.Report(1f);
                return new TexturePaintPluginCommit(new List<TexturePaintPluginCommit.LayerBinding>(),
                    replacements, dirtyPixels, commands.Count);
            }
            catch
            {
                for (int i = swapped - 1; i >= 0; i--)
                {
                    TexturePaintPluginCommit.LayerReplacement replacement = replacements[i];
                    replacement.set.layers[replacement.index] = replacement.before;
                    replacement.set.activeLayerIndex = replacement.index;
                    try { replacement.set.RecomposeAll(); replacement.set.BindPreviewTextures(); }
                    catch (Exception) { }
                }
                for (int i = 0; i < replacements.Count; i++) replacements[i].after?.Dispose();
                throw;
            }
            finally
            {
                foreach (Texture2D mask in geometryMasks.Values) Destroy(mask);
            }
        }

        public static TexturePaintPluginCommit CommitIntoLayerMasks(TextureStore store,
            TexturePaintPluginDescriptor descriptor,
            IReadOnlyList<TexturePaintPluginTileCommand> commands,
            IReadOnlyDictionary<TextureSet, TexturePaintLayer> destinations,
            System.Threading.CancellationToken token, IProgress<float> progress,
            TexturePaintPluginParameterSet parameters)
        {
            if (destinations == null || destinations.Count == 0)
                throw new InvalidOperationException(
                    "Layer-mask plugin execution requires at least one destination layer.");
            commands ??= Array.Empty<TexturePaintPluginTileCommand>();
            var replacements = new List<TexturePaintPluginCommit.LayerReplacement>();
            var bySet = new Dictionary<TextureSet, TexturePaintPluginCommit.LayerReplacement>();
            var geometryMasks = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            long dirtyPixels = 0L;
            int swapped = 0;
            try
            {
                foreach (KeyValuePair<TextureSet, TexturePaintLayer> pair in destinations)
                {
                    TextureSet set = pair.Key;
                    TexturePaintLayer before = pair.Value;
                    int index = set?.layers.IndexOf(before) ?? -1;
                    if (set == null || before?.layerMask?.target?.Front == null || index < 0)
                        throw new InvalidOperationException(
                            "A logical destination layer is missing its editable mask.");
                    TexturePaintLayer after = set.CloneLayer(before, before.name, true);
                    after.layerMask.pluginId = descriptor.id;
                    after.layerMask.pluginVersion = descriptor.pluginVersion;
                    after.layerMask.pluginParameters = parameters?.Clone() ??
                        new TexturePaintPluginParameterSet();
                    after.layerMask.pluginParametersJson =
                        JsonUtility.ToJson(after.layerMask.pluginParameters);
                    after.layerMask.pluginStale = false;
                    after.layerMask.pluginLastError = null;
                    var replacement = new TexturePaintPluginCommit.LayerReplacement
                    {
                        set = set, before = before, after = after, index = index
                    };
                    replacements.Add(replacement);
                    bySet.Add(set, replacement);
                }

                for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    TexturePaintPluginTileCommand command = commands[commandIndex];
                    if (command.target != TexturePaintPluginTarget.LayerMask)
                        throw new InvalidOperationException(
                            "A layer-content command cannot be committed into a layer mask.");
                    if (command.colorSpace != TexturePaintPluginColorSpace.Data)
                        throw new InvalidOperationException("Layer-mask output must use Data color space.");
                    TextureSet set = FindSet(store, command.surfaceId);
                    if (!bySet.TryGetValue(set, out TexturePaintPluginCommit.LayerReplacement replacement))
                        throw new InvalidOperationException(
                            "Plugin submitted mask output outside the selected logical layer.");
                    EditableTextureTarget target = replacement.after.layerMask.target;
                    if (command.rect.xMin < 0 || command.rect.yMin < 0 ||
                        command.rect.xMax > target.Width || command.rect.yMax > target.Height)
                        throw new InvalidOperationException("Plugin mask tile lies outside the target mask.");
                    string maskKey = set.persistentId + "|mask|" + target.Width + "x" + target.Height;
                    if (!geometryMasks.TryGetValue(maskKey, out Texture2D geometryMask))
                    {
                        geometryMask = TexturePaintGeometryMask.Build(set.surface, target.Width,
                            target.Height, null, -1, null);
                        geometryMasks.Add(maskKey, geometryMask);
                    }
                    ApplyMask(target, command, geometryMask, set.channelPackShader);
                    dirtyPixels += (long)command.rect.width * command.rect.height;
                    progress?.Report(0.25f + 0.7f * ((commandIndex + 1f) /
                        Mathf.Max(1, commands.Count)));
                }

                for (int i = 0; i < replacements.Count; i++)
                {
                    TexturePaintPluginCommit.LayerReplacement replacement = replacements[i];
                    replacement.set.layers[replacement.index] = replacement.after;
                    swapped++;
                    replacement.set.activeLayerIndex = replacement.index;
                    replacement.set.RecomposeAll();
                    replacement.set.BindPreviewTextures();
                }
                progress?.Report(1f);
                return new TexturePaintPluginCommit(new List<TexturePaintPluginCommit.LayerBinding>(),
                    replacements, dirtyPixels, commands.Count);
            }
            catch
            {
                for (int i = swapped - 1; i >= 0; i--)
                {
                    TexturePaintPluginCommit.LayerReplacement replacement = replacements[i];
                    replacement.set.layers[replacement.index] = replacement.before;
                    try { replacement.set.RecomposeAll(); replacement.set.BindPreviewTextures(); }
                    catch (Exception) { }
                }
                for (int i = 0; i < replacements.Count; i++) replacements[i].after?.Dispose();
                throw;
            }
            finally
            {
                foreach (Texture2D mask in geometryMasks.Values) Destroy(mask);
            }
        }

        private static void ClearChannels(TexturePaintLayer layer)
        {
            if (layer == null) return;
            foreach (EditableTextureTarget target in layer.channels.Values) target?.Dispose();
            layer.channels.Clear();
        }

        private static void LinkPluginLayers(List<TexturePaintPluginCommit.LayerBinding> bindings,
            TexturePaintLogicalLayerController logicalLayers)
        {
            if (logicalLayers == null || bindings == null || bindings.Count == 0) return;
            var groups = new Dictionary<TexturePaintLogicalTarget, List<TexturePaintPluginCommit.LayerBinding>>();
            int commandLayerCount = bindings.Count;
            for (int i = 0; i < commandLayerCount; i++)
            {
                TexturePaintPluginCommit.LayerBinding binding = bindings[i];
                TexturePaintLogicalTarget target = logicalLayers.FindTarget(binding.set);
                if (target == null) continue;
                if (!groups.TryGetValue(target, out List<TexturePaintPluginCommit.LayerBinding> group))
                { group = new List<TexturePaintPluginCommit.LayerBinding>(); groups.Add(target, group); }
                group.Add(binding);
            }
            foreach (KeyValuePair<TexturePaintLogicalTarget, List<TexturePaintPluginCommit.LayerBinding>> pair in groups)
            {
                string logicalLayerId = Guid.NewGuid().ToString("N");
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    pair.Value[i].layer.logicalLayerId = logicalLayerId;
                    pair.Value[i].layer.paintTargetId = pair.Key.id;
                }
                TexturePaintPluginCommit.LayerBinding primary = pair.Value[0];
                var created = new List<TexturePaintLogicalLayerMember>();
                if (!logicalLayers.LinkAndRepair(pair.Key, primary.set, primary.layer, created,
                    out TexturePaintLogicalLayerBinding logicalBinding) || !logicalBinding.complete)
                    throw new InvalidOperationException(logicalBinding?.error ??
                        $"Plugin output could not be linked across paint target '{pair.Key.displayName}'.");
                for (int i = 0; i < created.Count; i++)
                    bindings.Add(new TexturePaintPluginCommit.LayerBinding
                    {
                        set = created[i].textureSet,
                        layer = created[i].layer,
                        index = created[i].textureSet.layers.IndexOf(created[i].layer)
                    });
                logicalLayers.Activate(logicalBinding);
            }
        }

        private static void ValidateAll(TextureStore store, TexturePaintPluginDescriptor descriptor,
            IReadOnlyList<TexturePaintPluginTileCommand> commands)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                TexturePaintPluginTileCommand command = commands[i];
                if (!descriptor.Declares(command.channel)) throw new InvalidOperationException("Command targets an undeclared channel.");
                TextureSet set = FindSet(store, command.surfaceId);
                TextureChannelTarget target = set.GetChannel(command.channel);
                if (target == null) throw new InvalidOperationException($"Surface does not expose {command.channel}.");
                if (command.rect.xMin < 0 || command.rect.yMin < 0 || command.rect.xMax > target.Texture.width || command.rect.yMax > target.Texture.height)
                    throw new InvalidOperationException("Plugin tile lies outside the target texture.");
                bool colorChannel = TexturePaintChannelUtility.IsColor(command.channel);
                if (!colorChannel && command.colorSpace != TexturePaintPluginColorSpace.Data)
                    throw new InvalidOperationException($"{command.channel} requires Data color space.");
                if (colorChannel && command.colorSpace == TexturePaintPluginColorSpace.Data)
                    throw new InvalidOperationException($"{command.channel} requires Linear or SRGB color space.");
                if (command.channel == TexturePaintChannel.Normal && command.blend != TexturePaintPluginBlend.Replace)
                    throw new InvalidOperationException("Normal plugin commands must use Replace so vector-aware blending can be enforced.");
            }
        }

        private static void Apply(EditableTextureTarget target, TextureChannelTarget channel,
            TexturePaintPluginTileCommand command, Texture2D geometryMask,
            ComputeShader channelPackShader)
        {
            bool materialized = command.MaterializeCompactPixels();
            try { ApplyMaterialized(target, channel, command, geometryMask, channelPackShader); }
            finally { if (materialized) command.ReleaseMaterializedCompactPixels(); }
        }

        private static void ApplyMaterialized(EditableTextureTarget target, TextureChannelTarget channel,
            TexturePaintPluginTileCommand command, Texture2D geometryMask,
            ComputeShader channelPackShader)
        {
            if (TryApplyCompactGpu(target, command, geometryMask, channelPackShader, false)) return;
            Color[] destination = Read(target.Front, command.rect);
            Color[] maskPixels = geometryMask.GetPixels(command.rect.x, command.rect.y, command.rect.width, command.rect.height);
            for (int i = 0; i < destination.Length; i++)
            {
                Color source = Convert(command.GetPixel(i), command.colorSpace);
                source = TexturePaintChannelUtility.ConstrainColor(command.channel, source);
                float coverage = Mathf.Clamp01(command.opacity * maskPixels[i].r);
                if (command.channel == TexturePaintChannel.Normal)
                {
                    Vector3 from = DecodeNormal(destination[i]);
                    Vector3 to = DecodeNormal(source);
                    Vector3 normal = Vector3.Lerp(from, to, coverage).normalized;
                    destination[i] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f, Mathf.Lerp(destination[i].a, source.a, coverage));
                    continue;
                }
                switch (command.blend)
                {
                    case TexturePaintPluginBlend.Replace:
                        destination[i] = Color.Lerp(destination[i], source, coverage); break;
                    case TexturePaintPluginBlend.Add:
                        destination[i] = destination[i] + source * coverage; break;
                    case TexturePaintPluginBlend.Multiply:
                        destination[i] = Color.Lerp(destination[i], destination[i] * source, coverage); break;
                    default:
                        float alpha = Mathf.Clamp01(source.a * coverage);
                        destination[i] = new Color(
                            Mathf.Lerp(destination[i].r, source.r, alpha), Mathf.Lerp(destination[i].g, source.g, alpha),
                            Mathf.Lerp(destination[i].b, source.b, alpha), alpha + destination[i].a * (1f - alpha));
                        break;
                }
                destination[i] = TexturePaintChannelUtility.ConstrainColor(command.channel, destination[i]);
            }
            Texture2D patch = new Texture2D(command.rect.width, command.rect.height, target.Front.graphicsFormat,
                TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave, name = "Texture Paint Plugin Tile" };
            try
            {
                patch.SetPixels(destination); patch.Apply(false, false);
                Graphics.CopyTexture(patch, 0, 0, 0, 0, command.rect.width, command.rect.height,
                    target.Front, 0, 0, command.rect.x, command.rect.y);
                target.CopyFrontToBack(command.rect);
            }
            finally { Destroy(patch); }
        }

        private static void ApplyMask(EditableTextureTarget target,
            TexturePaintPluginTileCommand command, Texture2D geometryMask,
            ComputeShader channelPackShader)
        {
            bool materialized = command.MaterializeCompactPixels();
            try { ApplyMaterializedMask(target, command, geometryMask, channelPackShader); }
            finally { if (materialized) command.ReleaseMaterializedCompactPixels(); }
        }

        private static void ApplyMaterializedMask(EditableTextureTarget target,
            TexturePaintPluginTileCommand command, Texture2D geometryMask,
            ComputeShader channelPackShader)
        {
            if (TryApplyCompactGpu(target, command, geometryMask, channelPackShader, true)) return;
            Color[] destination = Read(target.Front, command.rect);
            Color[] geometry = geometryMask.GetPixels(command.rect.x, command.rect.y,
                command.rect.width, command.rect.height);
            for (int i = 0; i < destination.Length; i++)
            {
                float from = TexturePaintChannelUtility.ScalarValue(destination[i]);
                float source = TexturePaintChannelUtility.ScalarValue(command.GetPixel(i));
                float coverage = Mathf.Clamp01(command.opacity * geometry[i].r);
                float result;
                switch (command.blend)
                {
                    case TexturePaintPluginBlend.Add:
                        result = from + source * coverage; break;
                    case TexturePaintPluginBlend.Multiply:
                        result = Mathf.Lerp(from, from * source, coverage); break;
                    default:
                        result = Mathf.Lerp(from, source, coverage); break;
                }
                result = Mathf.Clamp01(result);
                destination[i] = new Color(result, result, result, 1f);
            }
            Texture2D patch = new Texture2D(command.rect.width, command.rect.height,
                target.Front.graphicsFormat, TextureCreationFlags.None)
            { hideFlags = HideFlags.HideAndDontSave, name = "Texture Paint Plugin Mask Tile" };
            try
            {
                patch.SetPixels(destination); patch.Apply(false, false);
                Graphics.CopyTexture(patch, 0, 0, 0, 0, command.rect.width,
                    command.rect.height, target.Front, 0, 0, command.rect.x, command.rect.y);
                target.CopyFrontToBack(command.rect);
            }
            finally { Destroy(patch); }
        }

        private static bool TryApplyCompactGpu(EditableTextureTarget target,
            TexturePaintPluginTileCommand command, Texture2D geometryMask,
            ComputeShader shader, bool maskMode)
        {
            if (target?.Front == null || target.Back == null || command?.compactPixels == null ||
                geometryMask == null ||
                shader == null || !SystemInfo.supportsComputeShaders ||
                !shader.HasKernel("CSApplyPluginTile")) return false;
            int kernel = shader.FindKernel("CSApplyPluginTile");
            if (!shader.IsSupported(kernel)) return false;

            Texture2D source = new Texture2D(command.rect.width, command.rect.height,
                TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "Texture Paint Plugin GPU Tile",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                source.SetPixels32(command.compactPixels);
                source.Apply(false, false);
                shader.SetInts("_TextureSize", target.Width, target.Height);
                shader.SetInts("_TileOffset", command.rect.x, command.rect.y);
                shader.SetInts("_DispatchSize", command.rect.width, command.rect.height);
                shader.SetInt("_PluginColorSpace", (int)command.colorSpace);
                shader.SetInt("_PluginBlend", (int)command.blend);
                shader.SetInt("_PluginChannel", (int)command.channel);
                shader.SetInt("_PluginMaskMode", maskMode ? 1 : 0);
                shader.SetFloat("_PluginOpacity", Mathf.Clamp01(command.opacity));
                shader.SetTexture(kernel, "_PluginSource", source);
                shader.SetTexture(kernel, "_PluginDestinationSource", target.Front);
                shader.SetTexture(kernel, "_GeometryMask", geometryMask);
                shader.SetTexture(kernel, "_Destination", target.Back);
                shader.Dispatch(kernel, Mathf.CeilToInt(command.rect.width / 16f),
                    Mathf.CeilToInt(command.rect.height / 16f), 1);
                target.SwapAndSynchronize(command.rect);
                return true;
            }
            finally { Destroy(source); }
        }

        private static Color Convert(Color color, TexturePaintPluginColorSpace source)
        {
            if (source == TexturePaintPluginColorSpace.Data) return color;
            if (source == TexturePaintPluginColorSpace.SRGB) return color.linear;
            return color;
        }

        private static Vector3 DecodeNormal(Color color)
        {
            Vector3 value = new Vector3(color.r * 2f - 1f, color.g * 2f - 1f, color.b * 2f - 1f);
            return value.sqrMagnitude > 0.000001f ? value.normalized : Vector3.forward;
        }

        private static Color[] Read(RenderTexture source, RectInt rect)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                RenderTexture.active = source;
                readback = new Texture2D(rect.width, rect.height, TextureFormat.RGBAFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave, name = "Texture Paint Plugin Readback" };
                readback.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixels();
            }
            finally
            {
                Destroy(readback);
                RenderTexture.active = previous;
            }
        }

        private static Color[] ReadScaled(RenderTexture source, int maximumResolution,
            out int width, out int height)
        {
            width = source.width;
            height = source.height;
            int largest = Mathf.Max(width, height);
            if (maximumResolution <= 0 || largest <= maximumResolution)
                return Read(source, new RectInt(0, 0, width, height));

            float scale = maximumResolution / (float)largest;
            width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            RenderTexture temporary = null;
            try
            {
                temporary = RenderTexture.GetTemporary(width, height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, temporary);
                return Read(temporary, new RectInt(0, 0, width, height));
            }
            finally
            {
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static Color[] Read(Texture2D source)
        {
            RenderTexture temporary = null;
            try
            {
                temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, temporary);
                return Read(temporary, new RectInt(0, 0, source.width, source.height));
            }
            finally
            {
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static Color[] ReadSprite(Sprite sprite, out int width, out int height)
        {
            if (sprite?.texture == null)
            { width = height = 0; return Array.Empty<Color>(); }
            Color[] atlas = Read(sprite.texture);
            Rect rect;
            try { rect = sprite.textureRect; }
            catch (InvalidOperationException)
            { rect = new Rect(0f, 0f, sprite.texture.width, sprite.texture.height); }
            int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.x), 0, sprite.texture.width - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(rect.y), 0, sprite.texture.height - 1);
            width = Mathf.Clamp(Mathf.RoundToInt(rect.width), 1, sprite.texture.width - x0);
            height = Mathf.Clamp(Mathf.RoundToInt(rect.height), 1, sprite.texture.height - y0);
            var result = new Color[width * height];
            for (int y = 0; y < height; y++)
                Array.Copy(atlas, (y0 + y) * sprite.texture.width + x0,
                    result, y * width, width);
            return result;
        }

        private static TextureSet FindSet(TextureStore store, string surfaceId)
        {
            if (store != null)
                for (int i = 0; i < store.Sets.Count; i++)
                    if (string.Equals(store.Sets[i].persistentId, surfaceId, StringComparison.Ordinal)) return store.Sets[i];
            throw new InvalidOperationException("Plugin command references an unknown surface: " + surfaceId);
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
