using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public static partial class UMAResourceReuse
    {
        internal static void PrepareCallbackMaterials(UMAData data)
        {
            foreach (var gm in data.generatedMaterials.materials)
            {
                if (gm?.cachedFirstPass == null) continue;
                foreach (var fragment in gm.materialFragments)
                    if (fragment.slotData?.asset?.SlotAtlassed != null)
                    {
                        PrepareMaterialEdits(gm);
                        break;
                    }
            }
        }
        /// <summary>Share the fully configured outputs, never the mutable per-avatar recipe or skeleton.</summary>
        internal static void FinalizeSurfaces(UMAData data)
        {
            if (data == null) return;
            bool reuse = TexturesEnabled(data);
            for (int r = 0; r < data.RendererCount; r++)
            {
                var renderer = data.GetRenderer(r);
                if (renderer == null) continue;
                if (!reuse && !renderer.TryGetComponent<UMAResourceLeaseOwner>(out _)) continue;
                var materials = renderer.sharedMaterials;
                var holds = new List<IDisposable>();
                var generated = new List<UMAData.GeneratedMaterial>();
                try
                {
                    foreach (var gm in data.generatedMaterials.materials)
                    {
                        if (gm == null || gm.skinnedMeshRenderer != renderer) continue;
                        generated.Add(gm);
                        if (reuse && gm.umaMaterial != null &&
                            gm.umaMaterial.materialType != UMAMaterial.MaterialType.UseExistingMaterial)
                        {
                            ShareMaterial(gm, false, materials);
                            ShareMaterial(gm, true, materials);
                        }
                        if (gm.cachedFirstPass != null) holds.Add(gm.cachedFirstPass.Retain());
                        if (gm.cachedSecondPass != null) holds.Add(gm.cachedSecondPass.Retain());
                        if (gm.cachedAtlasBindings != null)
                            foreach (var atlas in gm.cachedAtlasBindings)
                                if (atlas != null) holds.Add(atlas.RetainForMaterials(gm.material, gm.secondPassMaterial));
                    }
                    renderer.sharedMaterials = materials;
                    UMAResourceLeaseOwner.Get(renderer).SetSurfaces(holds, generated);
                    holds = null;
                }
                finally { if (holds != null) foreach (var hold in holds) hold.Dispose(); }
            }
        }

        private static void ShareMaterial(UMAData.GeneratedMaterial gm, bool second, Material[] rendererMaterials)
        {
            var existing = second ? gm.cachedSecondPass : gm.cachedFirstPass;
            var material = second ? gm.secondPassMaterial : gm.material;
            if (material == null || existing != null) return;
            // A surface cannot outlive private generated textures owned by UMAData.
            // Keep those existing paths private when atlas reuse deliberately bypasses.
            if (gm.resultingAtlasList != null)
                for (int i = 0; i < gm.resultingAtlasList.Length; i++)
                    if (gm.resultingAtlasList[i] != null &&
                        (gm.cachedAtlasBindings == null || i >= gm.cachedAtlasBindings.Length || gm.cachedAtlasBindings[i] == null)) return;
            UMAGeneratedResourceKey key;
            try
            {
                using (var description = new Description())
                {
                    // Template identity also separates hidden/custom shader state not exposed
                    // as regular properties. Change its registered revision after such edits.
                    description.Asset(second ? gm.umaMaterial.secondPass : gm.umaMaterial.material);
                    description.Material(material, atlases: gm.cachedAtlasBindings);
                    description.MaterialInputs(gm);
                    key = description.Key("UMA.Material.final.v1");
                }
            }
            catch (NotSupportedException) { return; } // Does not veto atlas sharing.
            var lease = UMAGeneratedResourceCache.Shared.Acquire<Material>(key);
            try
            {
                if (lease.IsBuilder)
                {
                    var dependencies = new List<IDisposable>();
                    try
                    {
                        if (gm.cachedAtlasBindings != null)
                            foreach (var atlas in gm.cachedAtlasBindings)
                                if (atlas != null) dependencies.Add(atlas.RetainForMaterials(material));
                        lease.Publish(material, destroy: value =>
                        {
                            UMAUtils.DestroySceneObject(value);
                            foreach (var dependency in dependencies) dependency.Dispose();
                        });
                    }
                    catch { foreach (var dependency in dependencies) dependency.Dispose(); throw; }
                }
                else
                {
                    for (int i = 0; i < rendererMaterials.Length; i++)
                        if (rendererMaterials[i] == material) rendererMaterials[i] = lease.Resource;
                    UMAUtils.DestroySceneObject(material);
                }
                if (second) { gm.secondPassMaterial = lease.Resource; gm.cachedSecondPass = lease; }
                else { gm.material = lease.Resource; gm.cachedFirstPass = lease; }
                lease = null;
            }
            finally { lease?.Dispose(); }
        }

        /// <summary>Detach only material instances before the builder changes their properties.
        /// Atlases remain shared; changing surface parameters must not force new textures.</summary>
        internal static void PrepareMaterialEdits(UMAData.GeneratedMaterial gm, bool editFirstPass = true)
        {
            if (gm == null) return;
            if (editFirstPass && gm.cachedFirstPass != null)
            {
                var copy = UnityEngine.Object.Instantiate(gm.material);
                gm.cachedFirstPass.Dispose(); gm.cachedFirstPass = null; gm.material = copy;
            }
            if (gm.cachedSecondPass != null)
            {
                var copy = UnityEngine.Object.Instantiate(gm.secondPassMaterial);
                gm.cachedSecondPass.Dispose(); gm.cachedSecondPass = null; gm.secondPassMaterial = copy;
            }
        }

        internal static void ReleaseSurfaceReferences(UMAData.GeneratedMaterial gm)
        {
            if (gm == null) return;
            gm.cachedFirstPass?.Dispose(); gm.cachedFirstPass = null;
            gm.cachedSecondPass?.Dispose(); gm.cachedSecondPass = null;
            if (gm.cachedAtlasBindings != null)
            {
                foreach (var binding in gm.cachedAtlasBindings) binding?.Dispose();
                gm.cachedAtlasBindings = null;
            }
        }
    }
}
