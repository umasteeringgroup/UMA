using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    [CreateAssetMenu(menuName = "UMA/Overlay Painter/Region Profile", fileName = "Region Profile")]
    public sealed class TexturePaintRegionProfile : ScriptableObject
    {
        [TextArea] public string description;
        public List<TexturePaintAnatomicalSettings> regions = Defaults();
        public TexturePaintAnatomicalSettings Get(TexturePaintAnatomicalRegion region)
            => regions?.Find(entry => entry != null && entry.region == region);
        private static List<TexturePaintAnatomicalSettings> Defaults()
        {
            var result = new List<TexturePaintAnatomicalSettings>();
            foreach (TexturePaintAnatomicalRegion region in Enum.GetValues(typeof(TexturePaintAnatomicalRegion)))
                result.Add(new TexturePaintAnatomicalSettings { region = region });
            return result;
        }
    }
}
