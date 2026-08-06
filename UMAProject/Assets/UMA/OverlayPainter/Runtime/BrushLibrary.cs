using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    [CreateAssetMenu(menuName = "UMA/Overlay Painter/Brush Library", fileName = "Overlay Painter Brush Library")]
    public sealed class BrushLibrary : ScriptableObject
    {
        [SerializeField] private List<BrushPreset> brushes = new List<BrushPreset>();
        public IReadOnlyList<BrushPreset> Brushes => brushes;

        public bool Add(BrushPreset preset)
        {
            if (preset == null || brushes.Contains(preset)) return false;
            brushes.Add(preset);
            return true;
        }

        public bool Remove(BrushPreset preset) => brushes.Remove(preset);
        public bool Insert(int index, BrushPreset preset)
        {
            if (preset == null || brushes.Contains(preset)) return false;
            brushes.Insert(Mathf.Clamp(index, 0, brushes.Count), preset);
            return true;
        }
        public void RemoveNullEntries() => brushes.RemoveAll(x => x == null);
    }
}
