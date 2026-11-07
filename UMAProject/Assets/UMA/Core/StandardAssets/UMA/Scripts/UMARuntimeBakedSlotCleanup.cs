using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    // Slot copies share this managed lifetime token. Native assets must be destroyed
    // on the main thread, after every recipe, NPC template and pending build releases it.
    internal sealed class UMARuntimeBakedSlotReference { }

    [AddComponentMenu("")]
    internal sealed class UMARuntimeBakedSlotCleanup : MonoBehaviour
    {
        private static readonly Dictionary<SlotDataAsset, WeakReference<UMARuntimeBakedSlotReference>> owned = new();
        private static readonly List<SlotDataAsset> released = new();
        private static UMARuntimeBakedSlotCleanup runner;
        private double nextCollection;

        internal static void Register(SlotData slot)
        {
            if (slot?.asset == null) return;
            if (!owned.ContainsKey(slot.asset)) owned.Add(slot.asset, new WeakReference<UMARuntimeBakedSlotReference>(null));
            slot.runtimeBakedSlotReference = Retain(slot.asset);
            if (Application.isPlaying && runner == null)
            {
                // DontSave objects can survive Editor Play-mode teardown. This
                // runtime-only host needs normal destruction when the session ends.
                var host = new GameObject("UMA Runtime Baked Slots") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(host);
                runner = host.AddComponent<UMARuntimeBakedSlotCleanup>();
            }
        }

        internal static UMARuntimeBakedSlotReference Retain(SlotDataAsset asset)
        {
            if (asset == null || !owned.TryGetValue(asset, out var reference)) return null;
            if (!reference.TryGetTarget(out var owner))
            {
                owner = new UMARuntimeBakedSlotReference();
                reference.SetTarget(owner);
            }
            return owner;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartupAsDouble < nextCollection) return;
            nextCollection = Time.realtimeSinceStartupAsDouble + .25;
            CollectReleased();
        }

        internal static void CollectReleased()
        {
            released.Clear();
            foreach (var pair in owned)
                if (!pair.Value.TryGetTarget(out _)) released.Add(pair.Key);
            foreach (var asset in released)
            {
                owned.Remove(asset);
                DestroyOwnedAsset(asset);
            }
            released.Clear();
        }

        private static void DestroyOwnedAsset(SlotDataAsset asset)
        {
            if (asset == null) return;
#if UNITY_EDITOR
            // An authoring tool may have deliberately saved the generated asset.
            if (UnityEditor.EditorUtility.IsPersistent(asset)) return;
#endif
            var indexer = UMAAssetIndexer.bareInstance;
            if (indexer != null)
            {
                var item = indexer.GetAssetItemForObject(asset);
                // Forced bakes replace the same index entry. Never remove a newer bake.
                if (item != null && ReferenceEquals(item._SerializedItem, asset))
                {
                    // RemoveAsset is an Editor authoring API. Release only this
                    // runtime entry, without saving or rebuilding the asset index.
                    indexer.GetAssetDictionary(typeof(SlotDataAsset)).Remove(item._Name);
                    if ((uint)item.Index < (uint)indexer.SerializedItems.Count &&
                        ReferenceEquals(indexer.SerializedItems[item.Index], item))
                        indexer.SerializedItems[item.Index] = null;
                    if (item._Guid != null && indexer.GuidTypes.TryGetValue(item._Guid, out var guidItem) &&
                        ReferenceEquals(guidItem, item))
                        indexer.GuidTypes.Remove(item._Guid);
                    item._SerializedItem = null;
                }
            }
            UMAUtils.DestroySceneObject(asset);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            ReleaseAll();
            if (runner != null) UMAUtils.DestroySceneObject(runner.gameObject);
            runner = null;
        }

        private static void ReleaseAll()
        {
            foreach (var asset in owned.Keys) DestroyOwnedAsset(asset);
            owned.Clear();
            released.Clear();
        }

        private void OnDestroy()
        {
            if (runner != this) return;
            ReleaseAll();
            runner = null;
        }

        private void OnApplicationQuit() => ReleaseAll();
    }
}
