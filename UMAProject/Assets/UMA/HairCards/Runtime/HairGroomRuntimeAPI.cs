using System;
using UnityEngine;

namespace UMA.HairCards.Runtime
{
    // Own texture-bound instances so neither pass mutates the user's shared material assets.
    internal sealed class HairRuntimeMaterialSet : IDisposable
    {
        private Material[] owned = Array.Empty<Material>();
        internal Material[] Update(HairCardMeshBuildResult build, Material fallback)
        {
            int count = Mathf.Max(1, build?.materials.Count ?? 0);
            if (owned.Length != count) { Dispose(); owned = new Material[count]; }
            for (int i = 0; i < count; i++)
            {
                Material source = build != null && i < build.materials.Count ? build.materials[i] : null;
                if (source == null) source = fallback;
                if (source == null) { Destroy(owned[i]); owned[i] = null; continue; }
                if (owned[i] == null) owned[i] = new Material(source) { hideFlags = HideFlags.DontSave };
                else
                {
                    owned[i].shader = source.shader;
                    owned[i].CopyPropertiesFromMaterial(source);
                }
                owned[i].name = source.name + " (Generated Hair)";
                if (build != null && i < build.atlases.Count) build.atlases[i]?.ApplyTexturesTo(owned[i]);
            }
            return owned;
        }
        private static void Destroy(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
        }
        public void Dispose()
        {
            foreach (Material material in owned) Destroy(material);
            owned = Array.Empty<Material>();
        }
    }

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
            internal readonly HairRuntimeMaterialSet RenderMaterials = new HairRuntimeMaterialSet();

            public void Dispose()
            {
                Build?.Dispose();
                RenderMaterials.Dispose();
                Build = null;
                Evaluation = null;
            }
        }

        /// <summary>For world-gravity modifiers, pass the destination object's localToWorldMatrix.
        /// The output remains source-local. Omitted matrices evaluate in the canonical identity frame.</summary>
        public static GeneratedHair Generate(HairGroomAsset groom, int lodLevel = 0,
            bool includeChildren = true, Matrix4x4? sourceToWorld = null)
        {
            if (groom == null) throw new ArgumentNullException(nameof(groom));
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions
            {
                sourceToWorld = sourceToWorld ?? Matrix4x4.identity,
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
            renderer.sharedMaterials = generated.RenderMaterials.Update(generated.Build, fallbackMaterial);
        }
    }
}
