using System;
using UnityEngine;

namespace UMA.HairCards.Runtime
{
    /// <summary>
    /// Play-mode API for deterministic, provider-free hair-card generation. Applications may call
    /// this from character creation UI, streaming systems, or their own LOD controller.
    /// </summary>
    public static class HairGroomRuntimeAPI
    {
        public sealed class GeneratedHair : IDisposable
        {
            public HairEvaluationResult Evaluation { get; internal set; }
            public HairCardMeshBuildResult Build { get; internal set; }
            public Mesh Mesh => Build?.mesh;

            public void Dispose()
            {
                Build?.Dispose();
                Build = null;
                Evaluation = null;
            }
        }

        public static GeneratedHair Generate(HairGroomAsset groom, int lodLevel = 0,
            bool includeChildren = true)
        {
            if (groom == null) throw new ArgumentNullException(nameof(groom));
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions
            {
                lodLevel = Mathf.Max(0, lodLevel),
                includeChildren = includeChildren,
                includeGuideCards = true,
                applyConstraints = true,
                applyModifiers = true,
                applySculptLayers = true
            });
            return new GeneratedHair
            {
                Evaluation = evaluation,
                Build = HairCardMeshGenerator.Build(evaluation, groom.name + " Runtime Hair")
            };
        }

        public static void ApplyTo(GeneratedHair generated, MeshFilter filter, MeshRenderer renderer,
            Material fallbackMaterial = null)
        {
            if (generated == null) throw new ArgumentNullException(nameof(generated));
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            filter.sharedMesh = generated.Mesh;
            int materialCount = Mathf.Max(1, generated.Build?.materials.Count ?? 0);
            Material[] materials = new Material[materialCount];
            for (int i = 0; i < materialCount; i++)
            {
                Material material = generated.Build != null && i < generated.Build.materials.Count
                    ? generated.Build.materials[i]
                    : null;
                materials[i] = material != null ? material : fallbackMaterial;
            }
            renderer.sharedMaterials = materials;
        }
    }
}
