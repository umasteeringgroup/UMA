using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
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
}
