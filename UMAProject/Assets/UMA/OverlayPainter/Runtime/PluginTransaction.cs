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

        private readonly List<LayerBinding> layers;
        private bool applied = true;
        public long dirtyPixels { get; }
        public int commandCount { get; }

        internal TexturePaintPluginCommit(List<LayerBinding> layers, long dirtyPixels, int commandCount)
        { this.layers = layers; this.dirtyPixels = dirtyPixels; this.commandCount = commandCount; }

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
            applied = true;
        }

        public void Dispose()
        {
            if (applied) return;
            for (int i = 0; i < layers.Count; i++) layers[i].layer?.Dispose();
            layers.Clear();
        }
    }

    internal static class TexturePaintPluginTransactionExecutor
    {
        public static TexturePaintReadContextV2 Capture(TextureStore store, TexturePaintPluginDescriptor descriptor,
            System.Threading.CancellationToken token, IProgress<float> progress, long memoryBudgetBytes)
        {
            var images = new Dictionary<string, TexturePaintReadOnlyImage>(StringComparer.Ordinal);
            var surfaceIds = new List<string>();
            if (store == null) return new TexturePaintReadContextV2(images, surfaceIds);
            int total = Mathf.Max(1, store.Sets.Count * 7), completed = 0;
            long capturedBytes = 0L;
            for (int setIndex = 0; setIndex < store.Sets.Count; setIndex++)
            {
                TextureSet set = store.Sets[setIndex];
                string surfaceId = set.persistentId ?? set.surface?.index.ToString() ?? setIndex.ToString();
                surfaceIds.Add(surfaceId);
                for (int channelIndex = 0; channelIndex < 7; channelIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    TexturePaintChannel channel = (TexturePaintChannel)channelIndex;
                    if (descriptor.Declares(channel))
                    {
                        TextureChannelTarget target = set.GetChannel(channel);
                        RenderTexture source = set.GetVisibleTexture(channel);
                        if (target != null && source != null)
                        {
                            Color[] pixels = Read(source, new RectInt(0, 0, source.width, source.height));
                            capturedBytes += pixels.LongLength * 16L;
                            if (capturedBytes > memoryBudgetBytes) throw new InvalidOperationException("Plugin snapshot memory budget exceeded.");
                            images[TexturePaintReadContextV2.Key(surfaceId, channel)] =
                                new TexturePaintReadOnlyImage(surfaceId, channel, source.width, source.height, target.sRGB, pixels);
                        }
                    }
                    completed++; progress?.Report(completed / (float)total * 0.25f);
                }
            }
            return new TexturePaintReadContextV2(images, surfaceIds);
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
                        layer.pluginId = descriptor.id;
                        layer.pluginVersion = descriptor.pluginVersion;
                        layer.pluginParametersJson = JsonUtility.ToJson(parameters ?? new TexturePaintPluginParameterSet());
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
                    Apply(target, baseTarget, command, geometryMask);
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
            TexturePaintPluginTileCommand command, Texture2D geometryMask)
        {
            Color[] destination = Read(target.Front, command.rect);
            Color[] maskPixels = geometryMask.GetPixels(command.rect.x, command.rect.y, command.rect.width, command.rect.height);
            for (int i = 0; i < destination.Length; i++)
            {
                Color source = Convert(command.pixels[i], command.colorSpace);
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
