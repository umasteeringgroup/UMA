using System.Collections.Generic;
using UnityEngine;

namespace UMA.Dismemberment
{
    /// <summary>Owns runtime meshes created for one detached piece.</summary>
    [DisallowMultipleComponent]
    public sealed class DismemberedPiece : MonoBehaviour
    {
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();

        public Transform TargetBone { get; private set; }
        public IReadOnlyList<SkinnedMeshRenderer> Renderers { get; private set; }

        internal void Initialize(Transform targetBone, List<SkinnedMeshRenderer> renderers,
            List<Mesh> meshes)
        {
            TargetBone = targetBone;
            Renderers = renderers != null ? renderers.ToArray() : new SkinnedMeshRenderer[0];
            ownedMeshes.Clear();
            if (meshes != null) ownedMeshes.AddRange(meshes);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < ownedMeshes.Count; i++)
            {
                Mesh mesh = ownedMeshes[i];
                if (mesh == null) continue;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
            ownedMeshes.Clear();
        }
    }
}
