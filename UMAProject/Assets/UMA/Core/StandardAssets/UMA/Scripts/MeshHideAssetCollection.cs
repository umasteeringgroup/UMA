using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    [CreateAssetMenu(menuName = "UMA/Misc/Mesh Hide Asset Collection", fileName = "MeshHideAssetCollection")]
    public class MeshHideAssetCollection : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField]
        private List<MeshHideAsset> assets = new List<MeshHideAsset>();

        [NonSerialized]
        private Dictionary<string, MeshHideAsset> bySlotName;

        [NonSerialized]
        private Dictionary<int, List<MeshHideAsset>> byHash;

        public IReadOnlyList<MeshHideAsset> Assets
        {
            get { return assets; }
        }

        public void OnBeforeSerialize()
        {
            // no-op; caches are rebuilt on demand
        }

        public void OnAfterDeserialize()
        {
            ClearCache();
        }

        public void ClearCache()
        {
            bySlotName = null;
            byHash = null;
        }

        private void EnsureCache()
        {
            if (bySlotName != null && byHash != null)
            {
                return;
            }

            bySlotName = new Dictionary<string, MeshHideAsset>(StringComparer.Ordinal);
            byHash = new Dictionary<int, List<MeshHideAsset>>();

            if (assets == null)
            {
                return;
            }

            for (int i = 0; i < assets.Count; i++)
            {
                MeshHideAsset mha = assets[i];
                if (mha == null)
                {
                    continue;
                }

                string slotName = mha.AssetSlotName;
                if (!string.IsNullOrEmpty(slotName))
                {
                    bySlotName[slotName] = mha;
                }

                int hash = mha.GetHashCode();
                List<MeshHideAsset> hashList;
                if (!byHash.TryGetValue(hash, out hashList))
                {
                    hashList = new List<MeshHideAsset>();
                    byHash.Add(hash, hashList);
                }
                hashList.Add(mha);
            }
        }

        public MeshHideAsset FindBySlotName(string slotName)
        {
            if (string.IsNullOrEmpty(slotName))
            {
                return null;
            }

            EnsureCache();
            MeshHideAsset asset;
            if (bySlotName.TryGetValue(slotName, out asset))
            {
                return asset;
            }
            return null;
        }

        public void AddOrUpdate(MeshHideAsset meshHideAsset)
        {
            if (meshHideAsset == null)
            {
                return;
            }

            string slotName = meshHideAsset.AssetSlotName;
            if (string.IsNullOrEmpty(slotName))
            {
                return;
            }

            if (assets == null)
            {
                assets = new List<MeshHideAsset>();
            }

            for (int i = assets.Count - 1; i >= 0; i--)
            {
                MeshHideAsset existing = assets[i];
                if (existing == null)
                {
                    assets.RemoveAt(i);
                    continue;
                }

                if (existing == meshHideAsset)
                {
                    continue;
                }

                if (string.Equals(existing.AssetSlotName, slotName, StringComparison.Ordinal))
                {
                    assets[i] = meshHideAsset;
                    ClearCache();
                    return;
                }
            }

            assets.Add(meshHideAsset);
            ClearCache();
        }

        public List<MeshHideAsset> FindByHashCode(int hashCode)
        {
            EnsureCache();
            List<MeshHideAsset> list;
            if (byHash.TryGetValue(hashCode, out list))
            {
                return list;
            }
            return null;
        }
    }
}
