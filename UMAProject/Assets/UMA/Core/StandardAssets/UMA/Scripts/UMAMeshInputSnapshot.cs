using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// An immutable, shared revision of a mesh input buffer. Keys refer to the revision,
    /// not another serialized copy of its vertices/weights/blendshapes. Public UMA arrays
    /// can be edited in place; invalidate source fingerprints after such edits.
    /// The lookup table owns neither the source nor its snapshot; live keys own snapshots.
    /// </summary>
    internal sealed class UMAMeshInputSnapshot
    {
        private sealed class Latest
        {
            internal WeakReference<UMAMeshInputSnapshot> Snapshot;
        }

        private static ConditionalWeakTable<object, Latest> latest = new ConditionalWeakTable<object, Latest>();
        private readonly byte[] bytes;
        internal readonly Hash128 Hash;

        private UMAMeshInputSnapshot(ReadOnlySpan<byte> source, Hash128? preparedHash)
        {
            bytes = source.ToArray();
            Hash = preparedHash ?? Hash128.Compute(bytes);
        }

        internal static UMAMeshInputSnapshot Capture(object sourceIdentity, ReadOnlySpan<byte> source, bool immutableSource = false)
        {
            var entry = latest.GetOrCreateValue(sourceIdentity);
            if (entry.Snapshot != null && entry.Snapshot.TryGetTarget(out var current) && (immutableSource || current.Matches(source)))
                return current;
            Hash128? preparedHash = immutableSource && UMAMeshPreparation.TryGetBufferHash(sourceIdentity, out var hash) ? hash : null;
            var snapshot = new UMAMeshInputSnapshot(source, preparedHash);
            if (entry.Snapshot == null) entry.Snapshot = new WeakReference<UMAMeshInputSnapshot>(snapshot);
            else entry.Snapshot.SetTarget(snapshot);
            return snapshot;
        }

        internal bool SameContents(UMAMeshInputSnapshot other) =>
            ReferenceEquals(this, other) || (other != null && Hash == other.Hash && Matches(other.bytes));

        private unsafe bool Matches(ReadOnlySpan<byte> source)
        {
            if (bytes.Length != source.Length) return false;
            if (bytes.Length == 0) return true;
            fixed (byte* a = bytes)
            fixed (byte* b = source)
                return UnsafeUtility.MemCmp(a, b, bytes.Length) == 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void StartSession() => latest = new ConditionalWeakTable<object, Latest>();
    }
}
