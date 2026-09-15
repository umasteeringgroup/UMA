using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    [CreateAssetMenu(menuName = "UMA/Hair Cards/Atlas Profile", fileName = "HairAtlasProfile")]
    public sealed class HairAtlasProfileAsset : ScriptableObject
    {
        [SerializeField] private string atlasId;
        public Texture2D albedo;
        public Texture2D normal;
        public Texture2D mask;
        public Material material;
        [Tooltip("Optional second draw of the same cards. Use a later render queue than the first pass when ordering is required.")]
        public Material secondPassMaterial;
        // Typed as SharedColorTable by the editor, keeping the geometry core independent of UMA_Core.
        public UnityEngine.Object sharedColorTable;
        public int sharedColorIndex = -1;
        public List<HairAtlasRegion> regions = new List<HairAtlasRegion>();

        public string AtlasId => atlasId;

        /// <summary>Bind atlas channels to an owned material instance; leave unset channels inherited.</summary>
        public void ApplyTexturesTo(Material target)
        {
            if (target == null) return;
            BindTexture(target, albedo, "_BaseMap"); BindTexture(target, albedo, "_BaseColorMap"); BindTexture(target, albedo, "_MainTex");
            BindTexture(target, normal, "_BumpMap"); BindTexture(target, normal, "_NormalMap"); BindTexture(target, normal, "_Normal");
            BindTexture(target, mask, "_MaskMap"); BindTexture(target, mask, "_Mask");
            BindTexture(target, mask, "_OcclusionMap");
        }

        private static void BindTexture(Material target, Texture texture, string property)
        {
            if (texture == null || !target.HasTexture(property)) return;
            target.SetTexture(property, texture);
            target.SetTextureScale(property, Vector2.one);
            target.SetTextureOffset(property, Vector2.zero);
        }

        public HairAtlasRegion GetWeightedRegion(uint randomValue)
        {
            return GetWeightedRegion(randomValue, HairAtlasRegionSelectionMode.All, null);
        }

        public HairAtlasRegion GetWeightedRegion(
            uint randomValue,
            HairAtlasRegionSelectionMode selectionMode,
            IReadOnlyList<string> selectedRegionIds)
        {
            if (regions == null || regions.Count == 0)
            {
                return null;
            }

            float total = 0f;
            int eligibleCount = 0;
            HairAtlasRegion lastEligible = null;
            for (int i = 0; i < regions.Count; i++)
            {
                HairAtlasRegion region = regions[i];
                if (!IsEligible(region, selectionMode, selectedRegionIds)) continue;
                total += Mathf.Max(0f, region.weight);
                eligibleCount++;
                lastEligible = region;
            }
            if (eligibleCount == 0) return null;
            if (total <= 1e-6f)
            {
                int selectedIndex = (int)(randomValue % (uint)eligibleCount);
                for (int i = 0; i < regions.Count; i++)
                {
                    HairAtlasRegion region = regions[i];
                    if (!IsEligible(region, selectionMode, selectedRegionIds)) continue;
                    if (selectedIndex-- == 0) return region;
                }
                return lastEligible;
            }

            float target = (randomValue / (float)uint.MaxValue) * total;
            for (int i = 0; i < regions.Count; i++)
            {
                HairAtlasRegion region = regions[i];
                if (!IsEligible(region, selectionMode, selectedRegionIds)) continue;
                target -= Mathf.Max(0f, region.weight);
                if (target <= 0f) return region;
            }
            return lastEligible;
        }

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref atlasId);
            regions ??= new List<HairAtlasRegion>();
            for (int i = 0; i < regions.Count; i++) regions[i]?.EnsureIntegrity();
        }

        public HairAtlasRegion CreateRegion(string regionName, Rect rectangle, float selectionWeight = 1f)
        {
            regions ??= new List<HairAtlasRegion>();
            HairAtlasRegion region = new HairAtlasRegion
            {
                name = string.IsNullOrWhiteSpace(regionName) ? "Region" : regionName,
                uvRect = rectangle,
                weight = Mathf.Max(0f, selectionWeight)
            };
            region.EnsureIntegrity();
            regions.Add(region);
            HairStableId.Ensure(ref atlasId);
            return region;
        }

        private void OnValidate()
        {
            EnsureIntegrity();
        }

        private static bool IsEligible(
            HairAtlasRegion region,
            HairAtlasRegionSelectionMode selectionMode,
            IReadOnlyList<string> selectedRegionIds)
        {
            if (region == null) return false;
            if (selectionMode == HairAtlasRegionSelectionMode.All) return true;
            if (selectedRegionIds == null) return false;
            for (int i = 0; i < selectedRegionIds.Count; i++)
            {
                if (string.Equals(region.Id, selectedRegionIds[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
