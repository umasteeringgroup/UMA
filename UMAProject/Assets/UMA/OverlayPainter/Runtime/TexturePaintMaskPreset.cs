using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    [CreateAssetMenu(menuName = "UMA/Overlay Painter/Mask Preset", fileName = "Overlay Painter Mask Preset")]
    public sealed class TexturePaintMaskPreset : ScriptableObject
    {
        public List<TexturePaintMask> masks = new List<TexturePaintMask>();

        public void ApplyTo(TexturePaintMaskStack stack)
        {
            if (stack == null) return;
            stack.Clear();
            for (int i = 0; i < masks.Count; i++)
            {
                if (masks[i] == null) continue;
                stack.Add(JsonUtility.FromJson<TexturePaintMask>(JsonUtility.ToJson(masks[i])));
            }
        }
    }
}
