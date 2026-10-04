using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    // Executes the real plugin against an immutable draft snapshot. No transaction is committed.
    internal sealed class TexturePaintPluginPreview : IDisposable
    {
        internal const int Resolution = 128;
        private const long Budget = 32L * 1024 * 1024;
        private readonly Action repaint;
        private CancellationTokenSource cancellation;
        private TextureStore store;
        private TextureSet set;
        private TexturePaintLayer layer;
        private ITexturePaintCommandExtensionV2 plugin;
        private TexturePaintPluginParameterSet parameters;
        private bool mask, pending, disposed;
        private string key;
        private int generation;
        private double due, lastDraw;
        private readonly TexturePaintPreviewDisplay display = new TexturePaintPreviewDisplay();
        internal Texture2D Texture => display.Texture;
        internal string Status { get; private set; } = "Preparing preview…";

        internal TexturePaintPluginPreview(Action repaint)
        {
            this.repaint = repaint;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        internal static bool Supports(ITexturePaintCommandExtensionV2 plugin)
            => plugin is ITexturePaintGeneratorV2 || plugin is ITexturePaintFilterV2;

        internal void Request(TextureStore store, TextureSet set, TexturePaintLayer layer,
            ITexturePaintCommandExtensionV2 plugin, TexturePaintPluginParameterSet values,
            bool mask, long documentVersion, bool force = false)
        {
            if (disposed) return;
            lastDraw = EditorApplication.timeSinceStartup;
            string next = set.persistentId + ":" + (layer?.id ?? "manager") + ":" + plugin.Descriptor.id + ":" +
                plugin.Descriptor.pluginVersion + ":" + mask + ":" + documentVersion + ":" + JsonUtility.ToJson(values);
            if (!force && next == key && ReferenceEquals(this.set, set) && ReferenceEquals(this.layer, layer) &&
                ReferenceEquals(this.plugin, plugin)) return;
            bool differentLayer = !ReferenceEquals(this.set, set) || this.layer?.id != layer?.id ||
                this.plugin?.Descriptor.id != plugin.Descriptor.id || this.mask != mask;
            key = next; generation++; cancellation?.Cancel();
            this.store = store; this.set = set; this.layer = layer; this.plugin = plugin;
            parameters = values.Clone(); parameters.EnsureDefaults(plugin.Descriptor); this.mask = mask;
            if (differentLayer) ClearImage();
            pending = true; due = lastDraw + .35; Status = "Updating preview…";
        }

        private void Update()
        {
            if (disposed) return;
            if (pending && cancellation == null && EditorApplication.timeSinceStartup >= due) Render();
        }

        private async void Render()
        {
            pending = false;
            int request = generation;
            var tokenSource = new CancellationTokenSource(); cancellation = tokenSource;
            tokenSource.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var result = await GenerateDraftAsync(store, set, layer, plugin, parameters, mask, tokenSource.Token);
                if (disposed || request != generation) return;
                display.Set(result.output, result.before, mask, plugin is ITexturePaintFilterV2);
                Status = result.output.Count == 0 ? "No output for these settings." : "128 × 128 draft";
            }
            catch (OperationCanceledException)
            {
                if (!disposed && request == generation) Status = "Preview timed out. Use Refresh to retry.";
            }
            catch (Exception exception)
            {
                if (!disposed && request == generation) { ClearImage(); Status = "Preview unavailable: " + exception.Message; }
            }
            finally
            {
                if (ReferenceEquals(cancellation, tokenSource)) cancellation = null;
                tokenSource.Dispose();
                if (!disposed) repaint?.Invoke();
            }
        }

        internal static async Task<Dictionary<TexturePaintChannel, Color[]>> GenerateAsync(
            TextureStore store, TextureSet set, TexturePaintLayer layer, ITexturePaintCommandExtensionV2 plugin,
            TexturePaintPluginParameterSet values, bool mask, CancellationToken token)
            => (await GenerateDraftAsync(store, set, layer, plugin, values, mask, token)).output;

        internal sealed class Draft
        {
            internal Dictionary<TexturePaintChannel, Color[]> output, before;
        }

        internal static async Task<Draft> GenerateDraftAsync(
            TextureStore store, TextureSet set, TexturePaintLayer layer, ITexturePaintCommandExtensionV2 plugin,
            TexturePaintPluginParameterSet values, bool mask, CancellationToken token)
        {
            if (!Supports(plugin)) throw new InvalidOperationException("This plugin has no image preview.");
            token.ThrowIfCancellationRequested();
            if (layer != null && !set.layers.Contains(layer)) throw new InvalidOperationException("The preview layer was removed.");
            var parameters = values.Clone(); parameters.EnsureDefaults(plugin.Descriptor);
            var reads = plugin is ITexturePaintDynamicChannelUsageV2 dynamicReads
                ? dynamicReads.ResolveReadChannels(parameters) : plugin.Descriptor.ResolvedReadChannels;
            var maps = plugin is ITexturePaintDynamicMeshMapUsageV2 dynamicMaps
                ? dynamicMaps.ResolveMeshMaps(parameters) : plugin.Descriptor.ResolvedMeshMaps;
            if ((reads & ~plugin.Descriptor.ResolvedReadChannels) != 0 || (maps & ~plugin.Descriptor.ResolvedMeshMaps) != 0)
                throw new InvalidOperationException("Plugin requested undeclared preview inputs.");
            var source = TexturePaintPluginTransactionExecutor.Capture(store, plugin.Descriptor, parameters,
                token, null, Budget, new Dictionary<TextureSet, TexturePaintLayer> { [set] = layer },
                mask ? TexturePaintChannelMask.None : reads, mask, maps, Resolution);
            var before = new Dictionary<TexturePaintChannel, Color[]>();
            if (mask)
                before[TexturePaintChannel.Custom] = source.GetMask(set.persistentId).CopyPixels();
            else
            {
                // Capture lighting inputs independently so plugins still receive only their declared reads.
                var lighting = TexturePaintPluginTransactionExecutor.Capture(store, plugin.Descriptor,
                    new TexturePaintPluginParameterSet(), token, null, Budget,
                    new Dictionary<TextureSet, TexturePaintLayer> { [set] = layer },
                    reads | TexturePaintChannelMask.Albedo | TexturePaintChannelMask.Normal |
                    TexturePaintChannelMask.NormalControl | TexturePaintChannelMask.Roughness |
                    TexturePaintChannelMask.Metallic | TexturePaintChannelMask.AmbientOcclusion,
                    false, TexturePaintMeshMapMask.None, Resolution);
                foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                {
                    var image = lighting.Get(set.persistentId, channel);
                    if (image != null) before[channel] = image.CopyPixels();
                }
            }
            var context = new TexturePaintCommandContextV2(plugin.Descriptor, source, parameters, token,
                null, Budget, mask ? TexturePaintPluginTarget.LayerMask : TexturePaintPluginTarget.LayerContent);
            await plugin.ExecuteAsync(context);
            token.ThrowIfCancellationRequested();
            return new Draft { output = Rasterize(context.SealAndSnapshot(), set.persistentId, mask, token,
                mask ? source.GetMask(set.persistentId) : null), before = before };
        }

        internal static Dictionary<TexturePaintChannel, Color[]> Rasterize(
            IReadOnlyList<TexturePaintPluginTileCommand> commands, string surfaceId, bool mask, CancellationToken token,
            TexturePaintReadOnlyMask initialMask = null)
        {
            var result = new Dictionary<TexturePaintChannel, Color[]>();
            foreach (var command in commands)
            {
                token.ThrowIfCancellationRequested();
                if (command.surfaceId != surfaceId) continue;
                if ((command.target == TexturePaintPluginTarget.LayerMask) != mask) continue;
                RectInt rect = command.rect;
                if (rect.xMin < 0 || rect.yMin < 0 || rect.xMax > Resolution || rect.yMax > Resolution)
                    throw new InvalidOperationException("Plugin output exceeds the draft dimensions.");
                if (!result.TryGetValue(command.channel, out var destination))
                {
                    result.Add(command.channel, destination = new Color[Resolution * Resolution]);
                    if (mask && initialMask != null)
                        for (int y = 0; y < Resolution; y++)
                        for (int x = 0; x < Resolution; x++)
                            destination[y * Resolution + x] = initialMask.GetPixel(x, y);
                }
                bool materialized = command.MaterializeCompactPixels();
                try
                {
                    for (int y = 0; y < rect.height; y++)
                    for (int x = 0; x < rect.width; x++)
                    {
                        Color source = command.GetPixel(y * rect.width + x);
                        int index = (rect.y + y) * Resolution + rect.x + x;
                        Color previous = destination[index];
                        float opacity = Mathf.Clamp01(command.opacity);
                        if (mask)
                        {
                            float from = TexturePaintChannelUtility.ScalarValue(previous);
                            float value = TexturePaintChannelUtility.ScalarValue(source);
                            float blended = command.blend == TexturePaintPluginBlend.Add ? from + value * opacity :
                                command.blend == TexturePaintPluginBlend.Multiply ? Mathf.Lerp(from, from * value, opacity) :
                                Mathf.Lerp(from, value, opacity);
                            blended = Mathf.Clamp01(blended);
                            destination[index] = new Color(blended, blended, blended, 1);
                            continue;
                        }
                        if (command.colorSpace == TexturePaintPluginColorSpace.SRGB) source = source.linear;
                        source = TexturePaintChannelUtility.ConstrainColor(command.channel, source);
                        if (command.channel == TexturePaintChannel.Normal)
                        {
                            Vector3 normal = Vector3.Lerp(DecodeNormal(previous), DecodeNormal(source), opacity).normalized;
                            destination[index] = new Color(normal.x * .5f + .5f, normal.y * .5f + .5f,
                                normal.z * .5f + .5f, Mathf.Lerp(previous.a, source.a, opacity));
                            continue;
                        }
                        switch (command.blend)
                        {
                            case TexturePaintPluginBlend.Replace: destination[index] = Color.Lerp(previous, source, opacity); break;
                            case TexturePaintPluginBlend.Add: destination[index] = previous + source * opacity; break;
                            case TexturePaintPluginBlend.Multiply: destination[index] = Color.Lerp(previous, previous * source, opacity); break;
                            default:
                                float alpha = Mathf.Clamp01(source.a * opacity);
                                destination[index] = PaintingEngine.CompositeStraightAlpha(previous, source, alpha); break;
                        }
                        destination[index] = TexturePaintChannelUtility.ConstrainColor(command.channel, destination[index]);
                    }
                }
                finally { if (materialized) command.ReleaseMaterializedCompactPixels(); }
            }
            return result;
        }

        private static Vector3 DecodeNormal(Color color)
        {
            var normal = new Vector3(color.r * 2 - 1, color.g * 2 - 1, color.b * 2 - 1);
            return normal.sqrMagnitude > .000001f ? normal.normalized : Vector3.forward;
        }

        internal void Draw(Action regenerateLayer = null, bool canRegenerate = true)
        {
            if (display.Draw(Status, "Draft output; Lit Surface uses a fixed studio light. Fine details need full regeneration.",
                    regenerateLayer, canRegenerate))
            { key = null; generation++; cancellation?.Cancel(); }
        }

        private void ClearImage()
        {
            display.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; generation++; cancellation?.Cancel();
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            ClearImage();
            display.Dispose();
        }
    }
}
