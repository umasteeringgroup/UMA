using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA
{
    /// <summary>Main-thread, per-generator ownership. No resource-cache flushing or shared-asset edits.</summary>
    public static class GeneratorQualityRuntime
    {
        private sealed class Layer
        {
            public Object Owner;
            public int Priority;
            public long Order;
            public Action<GeneratorQualityConfiguration> Overlay;
        }
        private sealed class Context
        {
            public UMAGenerator Generator;
            public GeneratorQualityConfiguration Baseline, Applied, Pending;
            public readonly List<Layer> Layers = new List<Layer>();
            public readonly List<GeneratorQualityCharacterState> Characters = new List<GeneratorQualityCharacterState>();
            public readonly Queue<WeakReference<UMAData>> Rebuilds = new Queue<WeakReference<UMAData>>();
            public readonly Dictionary<UMAMeshCombiner, UMAMeshCombiner> OwnedCombiners = new Dictionary<UMAMeshCombiner, UMAMeshCombiner>();
            public GeneratorQualityRebuild RebuildMode;
            public int RebuildsPerFrame = 1, LastRebuildFrame = -1;
            public int Revision;
            public bool Retiring, Pumping;
        }
        private static readonly Dictionary<UMAGenerator, Context> contexts = new Dictionary<UMAGenerator, Context>();
        private static long order;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (var context in contexts.Values) Dispose(context, true);
            contexts.Clear(); order = 0;
        }
        public static bool IsPending(UMAGenerator generator) => generator != null && contexts.TryGetValue(generator, out var c) && c.Pending != null;
        public static bool HasOwner(UMAGenerator generator, Object owner)
        {
            if (generator == null || !contexts.TryGetValue(generator, out var context)) return false;
            // Controller updates use this every frame; avoid a capturing predicate allocation.
            foreach (var layer in context.Layers) if (layer.Owner == owner) return true;
            return false;
        }
        public static void SetProfile(UMAGenerator generator, Object owner, GeneratorQualityProfile profile,
            int priority = 0, GeneratorQualityRebuild rebuild = GeneratorQualityRebuild.NewBuildsOnly, int rebuildsPerFrame = 1)
        {
            if (profile == null) { Remove(generator, owner); return; }
            Set(generator, owner, profile.Overlay, priority, rebuild, rebuildsPerFrame);
        }
        internal static void Set(UMAGenerator generator, Object owner, Action<GeneratorQualityConfiguration> overlay,
            int priority, GeneratorQualityRebuild rebuild, int rebuildsPerFrame)
        {
            if (generator == null || owner == null) return;
            if (!contexts.TryGetValue(generator, out var context))
            {
                var baseline = GeneratorQualityConfiguration.Capture(generator);
                context = new Context { Generator = generator, Baseline = baseline, Applied = baseline.Clone() };
                contexts.Add(generator, context);
            }
            var layer = context.Layers.Find(l => l.Owner == owner);
            if (layer == null) { layer = new Layer { Owner = owner, Order = ++order }; context.Layers.Add(layer); }
            layer.Priority = priority; layer.Overlay = overlay;
            context.Retiring = false;
            context.RebuildMode = rebuild; context.RebuildsPerFrame = Mathf.Max(1, rebuildsPerFrame);
            Compose(context);
            ApplyPending(generator);
        }
        public static void Remove(UMAGenerator generator, Object owner)
        {
            if (ReferenceEquals(generator, null) || !contexts.TryGetValue(generator, out var context)) return;
            if (context.Layers.RemoveAll(l => ReferenceEquals(l.Owner, owner) || l.Owner == null) == 0) return;
            Compose(context);
            ApplyPending(generator);
        }
        private static void Compose(Context context)
        {
            context.Layers.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Order.CompareTo(b.Order));
            var settings = context.Baseline.Clone();
            foreach (var layer in context.Layers) if (layer.Owner != null) layer.Overlay(settings);
            if (context.Layers.Count > 0)
                settings.ValidateAndResolveHardware(SystemInfo.systemMemorySize, SystemInfo.graphicsMemorySize);
            context.Pending = settings;
        }
        /// <summary>Called at generator boundaries. False means wait for outstanding work, not cancel it.</summary>
        public static bool ApplyPending(UMAGenerator generator)
        {
            if (generator == null || !contexts.TryGetValue(generator, out var context)) return true;
            if (context.Layers.RemoveAll(l => l.Owner == null) > 0) Compose(context);
            if (context.Pending != null)
            {
                if (!generator.CanApplyQualitySettings || RenderTexToCPU.renderTexturesToCPU.Count != 0 || RenderTexToCPU.PendingCopies() != 0)
                    return false;
                var next = context.Pending;
                bool outputChanged = !next.SameOutput(context.Applied);
                next.ApplyTo(generator);
                if (next.meshCombiner != null && !next.meshCombiner.gameObject.scene.IsValid())
                {
                    if (!context.OwnedCombiners.TryGetValue(next.meshCombiner, out var instance) || instance == null)
                    {
                        instance = Object.Instantiate(next.meshCombiner, generator.transform);
                        instance.gameObject.name = "UMA Quality Combiner";
                        instance.gameObject.hideFlags = HideFlags.DontSave;
                        context.OwnedCombiners[next.meshCombiner] = instance;
                    }
                    generator.meshCombiner = instance;
                }
                context.Pending = null; context.Applied = next;
                if (outputChanged)
                {
                    context.Revision++;
                    context.Rebuilds.Clear(); // coalesce rapid quality changes to the latest settings
                    if (context.RebuildMode != GeneratorQualityRebuild.NewBuildsOnly)
                        foreach (var data in Object.FindObjectsByType<UMAData>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                            if (data != null && data.inheritGeneratorQuality && data.umaGenerator == generator && data.RendererCount > 0)
                                context.Rebuilds.Enqueue(new WeakReference<UMAData>(data));
                }
                if (context.Layers.Count == 0)
                {
                    foreach (var state in context.Characters)
                    {
                        state.Restore();
                        if (outputChanged && context.RebuildMode == GeneratorQualityRebuild.NewBuildsOnly)
                            state.MarkOutputDirtyForNextBuild();
                    }
                    context.Retiring = true;
                }
            }
            PumpRebuilds(context);
            if (context.Retiring && !context.Pumping && context.Rebuilds.Count == 0 && generator.CanApplyQualitySettings)
            {
                Dispose(context, false);
                contexts.Remove(generator);
            }
            return true;
        }
        private static void PumpRebuilds(Context context)
        {
            if (context.Pumping || context.LastRebuildFrame == Time.frameCount || context.Rebuilds.Count == 0 ||
                !context.Generator.CanApplyQualitySettings) return;
            context.LastRebuildFrame = Time.frameCount;
            context.Pumping = true;
            int revision = context.Revision;
            try
            {
                int count = context.RebuildMode == GeneratorQualityRebuild.Immediate ? int.MaxValue : context.RebuildsPerFrame;
                while (count-- > 0 && context.Rebuilds.Count > 0 && context.Revision == revision && context.Pending == null)
                    if (context.Rebuilds.Dequeue().TryGetTarget(out var data) && data != null && data.inheritGeneratorQuality)
                    {
                        if (context.RebuildMode == GeneratorQualityRebuild.Immediate)
                        {
                            context.Generator.removeUMA(data);
                            data.isTextureDirty = data.isMeshDirty = true;
                            context.Generator.GenerateSingleUMA(data, true);
                        }
                        else data.Dirty(false, true, true);
                    }
            }
            finally { context.Pumping = false; }
        }
        internal static void BeforeBuild(UMAGenerator generator, UMAData data)
        {
            if (generator == null || data == null || !contexts.TryGetValue(generator, out var context)) return;
            if (context.Retiring) return;
            context.Characters.RemoveAll(s => !s.IsAlive);
            var state = context.Characters.Find(s => s.IsFor(data));
            if (!data.inheritGeneratorQuality) { state?.Restore(); return; }
            if (state == null) { state = new GeneratorQualityCharacterState(data, context.Baseline.defaultRendererAsset); context.Characters.Add(state); }
            if (state.Revision != context.Revision && (state.Revision >= 0 || data.RendererCount > 0))
                data.qualityNeedsFullBuild = data.isTextureDirty = data.isMeshDirty = true;
            state.Apply(context.Applied, context.Revision);
        }
        internal static void AfterBuild(UMAGenerator generator, UMAData data)
        {
            if (data != null) data.qualityNeedsFullBuild = false;
            if (generator == null || data == null || !contexts.TryGetValue(generator, out var context)) return;
            context.Characters.Find(s => s.IsFor(data))?.ApplyRenderers(context.Applied, generator);
        }
        internal static void Forget(UMAGenerator generator)
        {
            if (ReferenceEquals(generator, null) || !contexts.TryGetValue(generator, out var context)) return;
            Dispose(context, false); contexts.Remove(generator);
        }
        private static void Dispose(Context context, bool restoreGenerator)
        {
            if (restoreGenerator && context.Generator != null) context.Baseline.ApplyTo(context.Generator);
            foreach (var state in context.Characters) state.Restore();
            foreach (var combiner in context.OwnedCombiners.Values)
                if (combiner != null) UMAUtils.DestroySceneObject(combiner.gameObject);
            context.OwnedCombiners.Clear(); context.Characters.Clear(); context.Rebuilds.Clear(); context.Layers.Clear();
        }
    }
}
