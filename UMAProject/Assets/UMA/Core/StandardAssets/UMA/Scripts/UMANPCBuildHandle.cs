using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>An explicit immutable appearance request. Retain a handle for a spawn pool;
    /// dispose it when that pool ends. Live renderers own separate resource leases.</summary>
    public sealed class UMANPCBuildHandle : IDisposable
    {
        internal sealed class Entry
        {
            internal int References = 1, Epoch;
            internal DynamicCharacterAvatar Builder;
            internal UMANPCSnapshot Snapshot;
            internal string Failure;
            internal GeneratorQualityConfiguration Configuration;
            internal Action<DynamicCharacterAvatar> ApplyInputs;
            internal bool Pending = true;
        }
        internal Entry Value;
        private static readonly HashSet<Entry> entries = new HashSet<Entry>();
        internal UMANPCBuildHandle(DynamicCharacterAvatar builder)
        {
            Value = new Entry { Builder = builder, Epoch = UMANPCBuildQueue.Epoch, ApplyInputs = builder.CaptureNPCInputs() };
            entries.Add(Value);
        }
        private UMANPCBuildHandle(Entry entry) { Value = entry; entry.References++; }
        public bool IsReady => Value != null && Value.Epoch == UMANPCBuildQueue.Epoch && Value.Snapshot != null;
        public bool IsPending => Value != null && Value.Pending && Value.Epoch == UMANPCBuildQueue.Epoch;
        public bool IsInvalidated => Value != null && Value.Epoch != UMANPCBuildQueue.Epoch;
        public string FailureReason => Value == null ? "Disposed template handle" :
            Value.Failure ?? (Value.Epoch != UMANPCBuildQueue.Epoch ? "Template inputs invalidated" : null);
        public UMANPCBuildHandle Retain() => Value == null ? throw new ObjectDisposedException(nameof(UMANPCBuildHandle)) : new UMANPCBuildHandle(Value);
        public void Dispose()
        {
            var entry = Value; Value = null;
            if (entry == null || --entry.References != 0) return;
            entry.Snapshot?.Dispose(); entry.Snapshot = null;
            entry.Builder = null;
            entry.ApplyInputs = null; entries.Remove(entry);
        }
        public static void InvalidateAll() => UMANPCBuildQueue.Invalidate();
        internal static void Shutdown()
        {
            InvalidateAll();
            foreach (var entry in entries)
            {
                entry.Snapshot?.Dispose(); entry.Snapshot = null; entry.Builder = null;
                entry.ApplyInputs = null;
                entry.Pending = false; entry.Failure = "NPC template session ended.";
            }
            entries.Clear();
        }
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterReloadCleanup()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        }
#endif
    }

    // Separate from the UMA build queue: template instances use a time budget, not the
    // generator's expensive-build iteration limit. No hidden inactive DCA is retained.
    [AddComponentMenu("")]
    internal sealed class UMANPCBuildQueue : MonoBehaviour
    {
        internal static int Epoch;
        private static UMANPCBuildQueue instance;
        private readonly Queue<DynamicCharacterAvatar> requests = new Queue<DynamicCharacterAvatar>();
        internal static void Invalidate() { System.Threading.Interlocked.Increment(ref Epoch); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            UMANPCBuildHandle.Shutdown();
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
            Application.quitting -= UMANPCBuildHandle.Shutdown;
            Application.quitting += UMANPCBuildHandle.Shutdown;
        }
        internal static void Enqueue(DynamicCharacterAvatar avatar)
        {
            if (instance == null)
            {
                var go = new GameObject("UMA NPC Build Queue") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go); instance = go.AddComponent<UMANPCBuildQueue>();
            }
            instance.requests.Enqueue(avatar);
        }
        private void Update()
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            int count = requests.Count;
            while (count-- > 0 && requests.Count > 0)
            {
                var avatar = requests.Dequeue();
                if (avatar != null && !avatar.AdvanceNPCBuild()) requests.Enqueue(avatar);
                if (UMATime.StopwatchTicksToMilliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - start) >= 8) break;
            }
            if (requests.Count == 0) { instance = null; Destroy(gameObject); }
        }
        private void OnDestroy()
        {
            if (instance == this) instance = null;
            while (requests.Count > 0) { var avatar = requests.Dequeue(); if (avatar != null) avatar.CancelNPCBuild(); }
        }
    }
}
