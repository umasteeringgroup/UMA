using UnityEngine;
using System;
using System.Collections.Generic;

namespace UMA
{
    /// <summary>Resource ownership follows the generated renderer, including static conversions.</summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class UMAResourceLeaseOwner : MonoBehaviour
    {
        private UMAGeneratedResourceCache.Lease<Mesh> mesh;
        private List<IDisposable> surfaces = new List<IDisposable>();
        private List<UMAData.GeneratedMaterial> generated = new List<UMAData.GeneratedMaterial>();
        [SerializeField, HideInInspector] private List<UnityEngine.Object> privateResources = new List<UnityEngine.Object>();
        [SerializeField, HideInInspector] private string originalOwner;
        private void OnEnable()
        {
            var renderer = GetComponent<SkinnedMeshRenderer>();
            if (renderer == null) return;
            string identity = gameObject.GetUmaObjectId().ToString();
            if (!string.IsNullOrEmpty(originalOwner) && originalOwner != identity && privateResources.Count > 0)
                ClonePrivateResources(renderer);
            originalOwner = identity;
            // Unity clones native renderer references but not managed lease fields.
            // Adopt new counted ownership when instantiating a completed/static NPC.
            if (mesh == null) mesh = UMAGeneratedResourceCache.RetainSharedResource(renderer.sharedMesh);
            if (surfaces.Count == 0)
                foreach (var material in renderer.sharedMaterials)
                {
                    var lease = UMAGeneratedResourceCache.RetainSharedResource(material);
                    if (lease != null) surfaces.Add(lease);
                    else if (material != null)
                        foreach (var property in material.GetTexturePropertyNames())
                        {
                            var binding = UMACachedAtlas.RetainTextureBinding(material.GetTexture(property), material, property);
                            if (binding != null) surfaces.Add(binding);
                        }
                }
        }

        internal void AdoptNPCReferences() => OnEnable();
        // Unity does not dispatch OnDestroy to a never-active template renderer.
        internal void ReleaseNPCReferences() => OnDestroy();

        private static Texture CopyTexture(Texture source)
        {
            if (source is RenderTexture rt)
            {
                var output = new RenderTexture(rt.descriptor) { filterMode = rt.filterMode, wrapMode = rt.wrapMode,
                    anisoLevel = rt.anisoLevel, mipMapBias = rt.mipMapBias };
                output.Create(); Graphics.Blit(rt, output); return output;
            }
            return Instantiate(source);
        }

        private void ClonePrivateResources(SkinnedMeshRenderer renderer)
        {
            var copies = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            var owned = new List<UnityEngine.Object>();
            foreach (var resource in privateResources)
            {
                if (resource == null || copies.ContainsKey(resource)) continue;
                var copy = resource is Texture texture ? CopyTexture(texture) : Instantiate(resource);
                copies.Add(resource, copy); owned.Add(copy);
            }
            if (renderer.sharedMesh != null && copies.TryGetValue(renderer.sharedMesh, out var meshCopy)) renderer.sharedMesh = (Mesh)meshCopy;
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !copies.TryGetValue(materials[i], out var materialCopy)) continue;
                materials[i] = (Material)materialCopy;
                foreach (var property in materials[i].GetTexturePropertyNames())
                {
                    var texture = materials[i].GetTexture(property);
                    if (texture != null && copies.TryGetValue(texture, out var textureCopy)) materials[i].SetTexture(property, (Texture)textureCopy);
                }
            }
            renderer.sharedMaterials = materials; privateResources = owned;
        }
        internal void SetSurfaces(List<IDisposable> value, List<UMAData.GeneratedMaterial> materials)
        {
            var previous = surfaces; surfaces = value;
            generated = materials;
            foreach (var material in generated) AdoptPrivateStaticResources(material);
            foreach (var lease in previous) lease.Dispose();
        }
        private void Own(UnityEngine.Object resource)
        {
            privateResources.RemoveAll(value => value == null);
            if (!privateResources.Contains(resource)) privateResources.Add(resource);
        }
        internal void AdoptPrivateStaticResources(UMAData.GeneratedMaterial material)
        {
            if (material.umaMaterial != null && material.umaMaterial.materialType != UMAMaterial.MaterialType.UseExistingMaterial)
            {
                if (material.material != null && !UMAGeneratedResourceCache.IsManagedResource(material.material)) Own(material.material);
                if (material.secondPassMaterial != null && !UMAGeneratedResourceCache.IsManagedResource(material.secondPassMaterial)) Own(material.secondPassMaterial);
            }
            if (material.resultingAtlasList != null)
                foreach (var texture in material.resultingAtlasList)
                {
                    if (texture == null || UMAGeneratedResourceCache.IsManagedResource(texture)) continue;
                    if (texture is RenderTexture rt && (!RenderTexToCPU.SafeToFree(rt) ||
                        UMARenderTextureTracker.TryGetOwnership(rt, out var ownership) && ownership.temporary)) continue;
                    Own(texture);
                }
        }
        internal static UMAResourceLeaseOwner Get(SkinnedMeshRenderer renderer)
        {
            var owner = renderer.GetComponent<UMAResourceLeaseOwner>();
            if (owner == null) owner = renderer.gameObject.AddComponent<UMAResourceLeaseOwner>();
            owner.hideFlags = HideFlags.HideInInspector;
            return owner;
        }
        internal void SetMesh(UMAGeneratedResourceCache.Lease<Mesh> value)
        {
            if (ReferenceEquals(mesh, value)) return;
            mesh?.Dispose();
            mesh = value;
        }
        public static bool IsSharedMesh(SkinnedMeshRenderer renderer) => renderer != null &&
            renderer.TryGetComponent<UMAResourceLeaseOwner>(out var owner) && owner.mesh != null && owner.mesh.IsReady && owner.mesh.Resource == renderer.sharedMesh;

        internal static bool ReleaseMesh(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || !renderer.TryGetComponent<UMAResourceLeaseOwner>(out var owner) || owner.mesh == null) return false;
            bool owned = owner.mesh.Resource == renderer.sharedMesh;
            var lease = owner.mesh;
            owner.mesh = null;
            lease.Dispose();
            return owned;
        }

        /// <summary>Call before writing vertices/UVs/vertex colors or any other shared mesh data.</summary>
        public static Mesh MakeMeshUnique(SkinnedMeshRenderer renderer)
        {
            if (!IsSharedMesh(renderer)) return renderer != null ? renderer.sharedMesh : null;
            var copy = Instantiate(renderer.sharedMesh);
            ReleaseMesh(renderer);
            renderer.sharedMesh = copy;
            Get(renderer).Own(copy);
            return copy;
        }

        /// <summary>Make generated materials private before direct SetColor/SetFloat calls.
        /// A MaterialPropertyBlock is preferable for per-renderer shader overrides.</summary>
        public static void MakeMaterialsUnique(SkinnedMeshRenderer renderer)
        {
            if (renderer == null) return;
            var owner = Get(renderer);
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!UMAGeneratedResourceCache.IsManagedResource(materials[i])) continue;
                var original = materials[i];
                materials[i] = Instantiate(original); owner.Own(materials[i]);
                foreach (var gm in owner.generated)
                    if ((gm.material == original || gm.secondPassMaterial == original) && gm.cachedAtlasBindings != null)
                        foreach (var atlas in gm.cachedAtlasBindings)
                            if (atlas != null) owner.surfaces.Add(atlas.RetainForMaterials(materials[i]));
            }
            renderer.sharedMaterials = materials;
        }

        /// <summary>Copy one generated texture before painting/blitting into it. Does not change recipe inputs.</summary>
        public static Texture MakeTextureUnique(SkinnedMeshRenderer renderer, int materialIndex, string property)
        {
            // An explicit edit may wait, ordinary generation never waits for this.
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
            RenderTexToCPU.ApplyQueuedCopies(0);
            MakeMaterialsUnique(renderer);
            var material = renderer.sharedMaterials[materialIndex];
            var source = material.GetTexture(property);
            if (!UMAGeneratedResourceCache.IsManagedResource(source)) return source;
            Texture copy = CopyTexture(source);
            Get(renderer).Own(copy); material.SetTexture(property, copy); return copy;
        }

        // Managed leases cannot cross an assembly reload. Preserve visible output as
        // serialized private resources, then release the old cache deterministically.
        internal void DetachForAssemblyReload()
        {
            var renderer = GetComponent<SkinnedMeshRenderer>();
            if (renderer != null)
            {
                MakeMeshUnique(renderer);
                MakeMaterialsUnique(renderer);
                var materials = renderer.sharedMaterials;
                var copies = new Dictionary<Texture, Texture>();
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    foreach (var property in materials[i].GetTexturePropertyNames())
                    {
                        var texture = materials[i].GetTexture(property);
                        if (!UMAGeneratedResourceCache.IsManagedResource(texture)) continue;
                        if (!copies.TryGetValue(texture, out var copy))
                        {
                            copy = MakeTextureUnique(renderer, i, property); copies.Add(texture, copy);
                        }
                        else materials[i].SetTexture(property, copy);
                    }
                }
            }
            foreach (var material in generated) UMAResourceReuse.ReleaseSurfaceReferences(material);
            generated.Clear();
            foreach (var lease in surfaces) lease.Dispose(); surfaces.Clear();
        }
        private void OnDestroy()
        {
            var renderer = GetComponent<SkinnedMeshRenderer>();
            foreach (var material in generated)
                if (material.skinnedMeshRenderer == renderer) UMAResourceReuse.ReleaseSurfaceReferences(material);
            generated.Clear();
            mesh?.Dispose(); mesh = null;
            foreach (var lease in surfaces) lease.Dispose();
            surfaces.Clear();
            foreach (var resource in privateResources)
            {
                if (resource == null) continue;
                if (resource is RenderTexture rt) rt.Release();
                if (resource != null) UMAUtils.DestroySceneObject(resource);
            }
            privateResources.Clear();
        }
    }
}
