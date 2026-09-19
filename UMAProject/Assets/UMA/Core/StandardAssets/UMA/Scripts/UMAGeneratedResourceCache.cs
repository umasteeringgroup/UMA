using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Exact, session-local identity for an immutable generated output. The digest selects
    /// a bucket; equality also compares the complete description (not just its hash).
    /// </summary>
    public sealed class UMAGeneratedResourceKey : IEquatable<UMAGeneratedResourceKey>
    {
        private byte[] description;
        private int descriptionLength;
        private readonly bool isProbe;
        private readonly UMAMeshInputSnapshot[] meshInputs;
        private readonly UMAGeneratedResourceKey[] sourceKeys;
        private int hash;
        private readonly UMAGeneratedResourceKey firstStage;
        private Func<UMAGeneratedResourceKey> resolveDetails;
        private Func<bool> detailsStillValid;
        private UMAGeneratedResourceKey details;
        public string Kind { get; private set; }

        // Main-thread scratch lookup. Acquire freezes misses before inserting; hits never
        // allocate/copy a signature. A probe must never be retained as a dictionary key.
        internal UMAGeneratedResourceKey() { isProbe = true; }
        internal void SetProbe(string kind, byte[] buffer, int length)
        {
            if (!isProbe) throw new InvalidOperationException("Only scratch keys can be changed.");
            Kind = kind; description = buffer; descriptionLength = length;
            hash = Hash128.Compute(buffer, 0, length).GetHashCode() ^ StringComparer.Ordinal.GetHashCode(kind);
        }
        internal UMAGeneratedResourceKey Freeze()
        {
            if (!isProbe) return this;
            var bytes = new byte[descriptionLength];
            Array.Copy(description, bytes, descriptionLength);
            return new UMAGeneratedResourceKey(Kind, bytes, hash);
        }
        private UMAGeneratedResourceKey(string kind, byte[] bytes, int hash)
        {
            Kind = kind; description = bytes; descriptionLength = bytes.Length; this.hash = hash;
        }

        // Mesh requests freeze their small parameters immediately, but only inspect
        // source geometry when another request has the same first-stage signature.
        internal UMAGeneratedResourceKey(UMAGeneratedResourceKey firstStage,
            Func<UMAGeneratedResourceKey> resolveDetails, Func<bool> detailsStillValid)
        {
            Kind = "UMA.MeshRequest.v1";
            this.firstStage = firstStage ?? throw new ArgumentNullException(nameof(firstStage));
            this.resolveDetails = resolveDetails ?? throw new ArgumentNullException(nameof(resolveDetails));
            this.detailsStillValid = detailsStillValid ?? throw new ArgumentNullException(nameof(detailsStillValid));
            hash = firstStage.GetHashCode();
        }

        private UMAGeneratedResourceKey ResolveDetails()
        {
            if (details != null) return details;
            if (resolveDetails == null) return null;
            if (!detailsStillValid())
            {
                // Source edits revoke unresolved candidates. Identity comparison still
                // works, so old leases can always release their exact entry safely.
                resolveDetails = null; detailsStillValid = null;
                return null;
            }
            details = resolveDetails();
            resolveDetails = null; detailsStillValid = null;
            return details;
        }

        public UMAGeneratedResourceKey(string kind, byte[] description) : this(kind, description, null)
        {
        }

        internal UMAGeneratedResourceKey(string kind, byte[] description, UMAMeshInputSnapshot[] meshInputs,
            UMAGeneratedResourceKey[] sourceKeys = null)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            this.description = (byte[])(description ?? throw new ArgumentNullException(nameof(description))).Clone();
            descriptionLength = this.description.Length;
            this.meshInputs = meshInputs;
            this.sourceKeys = sourceKeys;
            // This hash only selects a bucket; Equals still checks every input exactly.
            // Avoid constructing a cryptographic provider for every main-thread lookup.
            hash = Hash128.Compute(this.description).GetHashCode() ^ StringComparer.Ordinal.GetHashCode(kind);
            if (meshInputs != null)
                foreach (var input in meshInputs) hash = unchecked(hash * 31 + input.Hash.GetHashCode());
            if (sourceKeys != null)
                foreach (var source in sourceKeys) hash = unchecked(hash * 31 + source.hash);
        }

        public bool Equals(UMAGeneratedResourceKey other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || hash != other.hash || Kind != other.Kind) return false;
            if (firstStage != null || other.firstStage != null)
            {
                if (firstStage == null || other.firstStage == null || !firstStage.Equals(other.firstStage)) return false;
                var exact = ResolveDetails();
                return exact != null && exact.Equals(other.ResolveDetails());
            }
            if (descriptionLength != other.descriptionLength) return false;
            if ((meshInputs?.Length ?? 0) != (other.meshInputs?.Length ?? 0)) return false;
            if ((sourceKeys?.Length ?? 0) != (other.sourceKeys?.Length ?? 0)) return false;
            for (int i = 0; i < descriptionLength; i++)
                if (description[i] != other.description[i]) return false;
            if (meshInputs != null)
                for (int i = 0; i < meshInputs.Length; i++)
                    if (!meshInputs[i].SameContents(other.meshInputs[i])) return false;
            if (sourceKeys != null)
                for (int i = 0; i < sourceKeys.Length; i++)
                    if (!sourceKeys[i].Equals(other.sourceKeys[i])) return false;
            return true;
        }
        public override bool Equals(object obj) => Equals(obj as UMAGeneratedResourceKey);
        public override int GetHashCode() => hash;
    }

    /// <summary>
    /// Owns immutable generated resources. All calls are main-thread only. Requests are
    /// counted reservations: the first builds, followers wait, and cancelling the builder
    /// promotes the next requester. A build must finish/dispose its jobs before releasing
    /// its reservation. No unused entries are retained.
    /// </summary>
    public sealed class UMAGeneratedResourceCache
    {
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<UMAGeneratedResourceKey, Entry> entries = new Dictionary<UMAGeneratedResourceKey, Entry>();
        private static readonly HashSet<UnityEngine.Object> ownedResources = new HashSet<UnityEngine.Object>();
        private static readonly Dictionary<UnityEngine.Object, Entry> publishedResources = new Dictionary<UnityEngine.Object, Entry>();
        public static bool IsManagedResource(UnityEngine.Object resource) => resource != null && ownedResources.Contains(resource);
        internal static void Protect(UnityEngine.Object resource) { if (resource != null) ownedResources.Add(resource); }
        internal static void Unprotect(UnityEngine.Object resource) { if (resource != null) ownedResources.Remove(resource); }
        public static UMAGeneratedResourceCache Shared { get; private set; } = new UMAGeneratedResourceCache();
        public long Hits { get; private set; }
        public long Misses { get; private set; }
        public long PendingJoins { get; private set; }
        public long Publications { get; private set; }
        [Serializable]
        public struct ResourceStatistics
        {
            public long Hits;
            public long Misses;
            public long PendingJoins;
            public long Publications;
            public long Bypasses;
            public string LastBypassReason;
            public long Requests => Hits + Misses + PendingJoins;
            public long Attempts => Requests + Bypasses;

            public static ResourceStatistics Since(ResourceStatistics current, ResourceStatistics baseline) =>
                new ResourceStatistics
                {
                    Hits = Math.Max(0, current.Hits - baseline.Hits),
                    Misses = Math.Max(0, current.Misses - baseline.Misses),
                    PendingJoins = Math.Max(0, current.PendingJoins - baseline.PendingJoins),
                    Publications = Math.Max(0, current.Publications - baseline.Publications),
                    Bypasses = Math.Max(0, current.Bypasses - baseline.Bypasses),
                    LastBypassReason = current.Bypasses > baseline.Bypasses ? current.LastBypassReason : null
                };
        }

        private readonly Dictionary<Type, ResourceStatistics> statisticsByType = new Dictionary<Type, ResourceStatistics>();

        /// <summary>Lookup/build counters only. Retaining a lease is not a cache hit.</summary>
        public ResourceStatistics GetStatistics<T>() where T : UnityEngine.Object =>
            statisticsByType.TryGetValue(typeof(T), out var value) ? value : default;

        public void RecordBypass<T>(string reason) where T : UnityEngine.Object
        {
            CheckThread();
            var statistics = GetStatistics<T>();
            statistics.Bypasses++;
            statistics.LastBypassReason = reason;
            statisticsByType[typeof(T)] = statistics;
        }
        public int EntryCount => entries.Count;
        public int ReferenceCount { get; private set; }
        public string DescribeEntries()
        {
            var result = new System.Text.StringBuilder();
            foreach (var entry in entries.Values)
                result.AppendLine(entry.Key.Kind + ": " + (entry.Resource != null ? entry.Resource.name : "building") + " / " + entry.Requests.Count + " owners");
            return result.ToString();
        }

        internal sealed class Entry
        {
            internal UMAGeneratedResourceCache Owner;
            internal UMAGeneratedResourceKey Key;
            internal Type Type;
            internal UnityEngine.Object Resource;
            internal object Metadata;
            internal Action<UnityEngine.Object> Destroy;
            internal Action CompleteBuild;
            internal bool Published;
            internal readonly LinkedList<object> Requests = new LinkedList<object>();
        }

        public sealed class Lease<T> : IDisposable where T : UnityEngine.Object
        {
            private UMAGeneratedResourceCache cache;
            private readonly Entry entry;
            private LinkedListNode<object> node;
            internal Lease(UMAGeneratedResourceCache cache, Entry entry)
            {
                this.cache = cache;
                this.entry = entry;
                node = entry.Requests.AddLast(this);
            }
            public bool IsReleased => cache == null;
            public bool IsReady => cache != null && entry.Resource != null;
            public bool IsBuilder => cache != null && !entry.Published && entry.Requests.First == node;
            public T Resource => IsReady ? (T)entry.Resource : null;
            public object Metadata => IsReady ? entry.Metadata : null;
            public Lease<T> Retain()
            {
                if (cache == null) throw new ObjectDisposedException(nameof(Lease<T>));
                cache.CheckThread();
                cache.ReferenceCount++;
                return new Lease<T>(cache, entry);
            }
            public void SetBuildCompletion(Action complete)
            {
                if (!IsBuilder) throw new InvalidOperationException("Only the builder can register its completion action.");
                entry.CompleteBuild = complete;
            }
            public void CompletePendingBuild()
            {
                if (cache == null) throw new ObjectDisposedException(nameof(Lease<T>));
                cache.CheckThread();
                if (!IsReady && !IsBuilder) entry.CompleteBuild?.Invoke();
            }

            /// <summary>Transfers ownership only on success. Never publish partial/mutable outputs.</summary>
            public void Publish(T resource, object metadata = null, Action<T> destroy = null)
            {
                if (cache == null) throw new ObjectDisposedException(nameof(Lease<T>));
                cache.CheckThread();
                if (!IsBuilder) throw new InvalidOperationException("Only the current builder may publish this resource.");
                if (resource == null) throw new ArgumentNullException(nameof(resource));
                if (!ownedResources.Add(resource)) throw new InvalidOperationException("A generated object already belongs to a cache entry.");
                entry.Resource = resource;
                entry.Published = true;
                publishedResources.Add(resource, entry);
                entry.Metadata = metadata;
                entry.CompleteBuild = null;
                entry.Destroy = destroy == null ? DestroyResource : value => destroy((T)value);
                cache.Publications++;
                var statistics = cache.GetStatistics<T>();
                statistics.Publications++;
                cache.statisticsByType[typeof(T)] = statistics;
            }

            public void Dispose()
            {
                if (cache == null) return;
                cache.CheckThread();
                var owner = cache;
                if (IsBuilder) entry.CompleteBuild = null;
                cache = null;
                entry.Requests.Remove(node);
                node = null;
                owner.ReferenceCount--;
                if (entry.Requests.Count != 0) return;
                if (owner.entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) owner.entries.Remove(entry.Key);
                // A caller may retain a disposed lease. It must not retain the source
                // snapshots after the last live owner has released the generated output.
                entry.Key = null;
                var resource = entry.Resource;
                entry.Resource = null;
                entry.Metadata = null;
                ownedResources.Remove(resource);
                if (!ReferenceEquals(resource, null)) publishedResources.Remove(resource);
                if (resource != null) entry.Destroy(resource);
                entry.Destroy = null;
            }
        }

        public Lease<T> Acquire<T>(UMAGeneratedResourceKey key) where T : UnityEngine.Object
        {
            CheckThread();
            if (key == null) throw new ArgumentNullException(nameof(key));
            var statistics = GetStatistics<T>();
            entries.TryGetValue(key, out var entry);
            if (entry != null && entry.Published && entry.Resource == null) { entries.Remove(key); entry = null; }
            if (entry == null)
            {
                key = key.Freeze();
                entry = new Entry { Key = key, Type = typeof(T), Owner = this };
                entries.Add(key, entry);
                Misses++;
                statistics.Misses++;
            }
            else
            {
                if (entry.Type != typeof(T)) throw new InvalidOperationException("A cache key cannot identify different resource types.");
                if (entry.Resource != null) { Hits++; statistics.Hits++; }
                else { PendingJoins++; statistics.PendingJoins++; }
            }
            statisticsByType[typeof(T)] = statistics;
            ReferenceCount++;
            return new Lease<T>(this, entry);
        }

        /// <summary>Stop admitting new users to an entry. Existing owners keep their
        /// resource until release, so invalidation never destroys a visible avatar.</summary>
        public void Invalidate(UMAGeneratedResourceKey key)
        {
            CheckThread();
            if (key == null) throw new ArgumentNullException(nameof(key));
            entries.Remove(key);
        }

        /// <summary>Retain an already shared output when explicitly copying a renderer.
        /// Returns null for borrowed source assets and private outputs.</summary>
        public static Lease<T> RetainSharedResource<T>(T resource) where T : UnityEngine.Object
        {
            if (resource == null || !publishedResources.TryGetValue(resource, out var entry) || entry.Type != typeof(T)) return null;
            entry.Owner.CheckThread(); entry.Owner.ReferenceCount++;
            return new Lease<T>(entry.Owner, entry);
        }

        private void CheckThread()
        {
            if (thread != Thread.CurrentThread.ManagedThreadId)
                throw new InvalidOperationException("Generated resource ownership must be changed on the Unity main thread.");
        }

        private static void DestroyResource(UnityEngine.Object resource)
        {
            if (resource is RenderTexture rt)
            {
                UMARenderTextureTracker.Untrack(rt);
                rt.Release();
            }
            UMAUtils.DestroySceneObject(resource);
        }

        // Old owners retain their old cache through their leases. Resetting a session must
        // never destroy a resource still bound to a renderer when scene reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void StartSession() => Shared = new UMAGeneratedResourceCache();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterReloadCleanup()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        }

        private static void BeforeAssemblyReload()
        {
            foreach (var generator in Resources.FindObjectsOfTypeAll<UMAGeneratorBuiltin>())
                if (generator != null && generator.gameObject.scene.IsValid()) generator.Clear();
            RenderTexToCPU.CleanupPendingCopies();
            foreach (var owner in Resources.FindObjectsOfTypeAll<UMAResourceLeaseOwner>())
                if (owner != null && owner.gameObject.scene.IsValid()) owner.DetachForAssemblyReload();
            foreach (var data in Resources.FindObjectsOfTypeAll<UMAData>())
                if (data != null && data.gameObject.scene.IsValid())
                    foreach (var material in data.generatedMaterials.materials) UMAResourceReuse.ReleaseSurfaceReferences(material);
        }
#endif
    }
}
