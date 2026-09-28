using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    [CreateAssetMenu(menuName = "UMA/Quality/Generator Quality Settings")]
    public sealed class GeneratorQualitySettings : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public bool anyPlatform = true;
            public RuntimePlatform platform = RuntimePlatform.WindowsPlayer;
            [Tooltip("Exact Unity quality name, or empty for every quality level. Indices are not stable across builds.")]
            public string qualityLevel;
            public GeneratorQualityProfile profile;
        }
        public GeneratorQualityProfile defaultProfile;
        public List<Entry> entries = new List<Entry>();

        /// <summary>Exact pair > platform default > all-platform quality > default. First duplicate wins.</summary>
        public GeneratorQualityProfile Resolve(RuntimePlatform platform, string qualityLevel)
        {
            var result = defaultProfile;
            int best = -1;
            if (entries == null) return result;
            foreach (var entry in entries)
            {
                if (entry == null || entry.profile == null || (!entry.anyPlatform && entry.platform != platform)) continue;
                bool anyQuality = string.IsNullOrEmpty(entry.qualityLevel);
                if (!anyQuality && !string.Equals(entry.qualityLevel, qualityLevel, StringComparison.Ordinal)) continue;
                int score = (entry.anyPlatform ? 0 : 2) + (anyQuality ? 0 : 1);
                if (score > best) { best = score; result = entry.profile; }
            }
            return result;
        }
    }
}
