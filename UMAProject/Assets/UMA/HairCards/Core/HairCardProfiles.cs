using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    [CreateAssetMenu(menuName = "UMA/Hair Cards/Card Profile", fileName = "HairCardProfile")]
    public sealed class HairCardProfileAsset : ScriptableObject
    {
        [SerializeField] private string profileId;
        [SerializeField] private HairCardShape shape = HairCardShape.Ribbon;
        [SerializeField, Range(3, 12)] private int tubeSides = 6;
        [SerializeField, Min(0f)] private float defaultWidth = 0.012f;
        [SerializeField, Min(0f)] private float tipWidth = 0f;
        [SerializeField, Range(2, 64)] private int samplesPerCard = 12;
        [SerializeField] private bool doubleSided = true;
        [SerializeField] private AnimationCurve widthAlongCard =
            AnimationCurve.Linear(0f, 1f, 1f, 0f);

        public string ProfileId => profileId;
        public HairCardShape Shape => shape;
        public int TubeSides => Mathf.Clamp(tubeSides, 3, 12);
        public float DefaultWidth => Mathf.Max(0f, defaultWidth);
        public float TipWidth => Mathf.Max(0f, tipWidth);
        public int SamplesPerCard => Mathf.Clamp(samplesPerCard, 2, 64);
        public bool DoubleSided => doubleSided;
        public AnimationCurve WidthAlongCard => widthAlongCard;

        public float EvaluateWidth(float normalizedLength)
        {
            float t = Mathf.Clamp01(normalizedLength);
            float curve = widthAlongCard != null ? Mathf.Max(0f, widthAlongCard.Evaluate(t)) : 1f - t;
            return Mathf.Lerp(TipWidth, DefaultWidth, curve);
        }

        public void Configure(
            HairCardShape cardShape,
            float rootWidth,
            float endWidth,
            int sampleCount,
            int sideCount = 6,
            bool generateBackfaces = true)
        {
            shape = cardShape;
            defaultWidth = Mathf.Max(0f, rootWidth);
            tipWidth = Mathf.Max(0f, endWidth);
            samplesPerCard = Mathf.Clamp(sampleCount, 2, 64);
            tubeSides = Mathf.Clamp(sideCount, 3, 12);
            doubleSided = generateBackfaces;
            HairStableId.Ensure(ref profileId);
        }

        private void OnValidate()
        {
            HairStableId.Ensure(ref profileId);
            tubeSides = Mathf.Clamp(tubeSides, 3, 12);
            defaultWidth = Mathf.Max(0f, defaultWidth);
            tipWidth = Mathf.Max(0f, tipWidth);
            samplesPerCard = Mathf.Clamp(samplesPerCard, 2, 64);
            widthAlongCard ??= AnimationCurve.Linear(0f, 1f, 1f, 0f);
        }
    }

    [Serializable]
    public sealed class HairAtlasRegion
    {
        [SerializeField] private string id;
        public string name = "Region";
        public Rect uvRect = new Rect(0f, 0f, 1f, 1f);
        [Min(0f)] public float weight = 1f;
        public string[] tags = Array.Empty<string>();
        public bool flipU;
        public bool flipV;

        public string Id => id;

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            weight = Mathf.Max(0f, weight);
            uvRect.x = Mathf.Clamp01(uvRect.x);
            uvRect.y = Mathf.Clamp01(uvRect.y);
            uvRect.width = Mathf.Clamp(uvRect.width, 0f, 1f - uvRect.x);
            uvRect.height = Mathf.Clamp(uvRect.height, 0f, 1f - uvRect.y);
            tags ??= Array.Empty<string>();
        }
    }

    [CreateAssetMenu(menuName = "UMA/Hair Cards/Atlas Profile", fileName = "HairAtlasProfile")]
    public sealed class HairAtlasProfileAsset : ScriptableObject
    {
        [SerializeField] private string atlasId;
        public Texture2D albedo;
        public Texture2D normal;
        public Texture2D mask;
        public Material material;
        public List<HairAtlasRegion> regions = new List<HairAtlasRegion>();

        public string AtlasId => atlasId;

        public HairAtlasRegion GetWeightedRegion(uint randomValue)
        {
            if (regions == null || regions.Count == 0)
            {
                return null;
            }

            float total = 0f;
            for (int i = 0; i < regions.Count; i++)
            {
                if (regions[i] != null) total += Mathf.Max(0f, regions[i].weight);
            }
            if (total <= 1e-6f)
            {
                return regions[(int)(randomValue % (uint)regions.Count)];
            }

            float target = (randomValue / (float)uint.MaxValue) * total;
            for (int i = 0; i < regions.Count; i++)
            {
                HairAtlasRegion region = regions[i];
                if (region == null) continue;
                target -= Mathf.Max(0f, region.weight);
                if (target <= 0f) return region;
            }
            return regions[regions.Count - 1];
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
            HairStableId.Ensure(ref atlasId);
            regions ??= new List<HairAtlasRegion>();
            for (int i = 0; i < regions.Count; i++) regions[i]?.EnsureIntegrity();
        }
    }
}
