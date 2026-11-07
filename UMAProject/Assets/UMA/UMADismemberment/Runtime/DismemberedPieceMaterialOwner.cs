using System.Collections.Generic;
using UnityEngine;

namespace UMA.Dismemberment
{
    /// <summary>Owns material clones needed to isolate a detached piece from a live source atlas.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class DismemberedPieceMaterialOwner : MonoBehaviour
    {
        private readonly List<Material> materials = new List<Material>();

        internal void Add(Material material)
        {
            if (material != null && !materials.Contains(material)) materials.Add(material);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < materials.Count; i++)
            {
                Material material = materials[i];
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            materials.Clear();
        }
    }
}
