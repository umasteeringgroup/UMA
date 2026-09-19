using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace UMA
{
    /// <summary>A sampled census of outputs referenced by the supplied live avatars, not all project assets.</summary>
    public static class UMAGeneratedResourceMemory
    {
        [Serializable]
        public struct Snapshot
        {
            public int Avatars;
            public int Renderers;
            public int UniqueMeshes;
            public int UniqueTextures;
            public int MeshUses;
            public int TextureUses;
            public int UnavailableMemorySizes;
            public long MeshBytes;
            public long TextureBytes;
            public long MeshBytesWithoutSharing;
            public long TextureBytesWithoutSharing;
            public long TotalBytes => MeshBytes + TextureBytes;
            public long AvoidedBytes => MeshBytesWithoutSharing + TextureBytesWithoutSharing - TotalBytes;
            public int SharedMeshUses => Math.Max(0, MeshUses - UniqueMeshes);
            public int SharedTextureUses => Math.Max(0, TextureUses - UniqueTextures);
        }

        public static Snapshot Capture(IEnumerable<UMAData> avatars, Func<UnityEngine.Object, long> measure = null)
        {
            if (avatars == null) throw new ArgumentNullException(nameof(avatars));
            measure ??= Profiler.GetRuntimeMemorySizeLong;
            var result = new Snapshot();
            var seenAvatars = new HashSet<UMAData>();
            var meshes = new Dictionary<Mesh, long>();
            var textures = new Dictionary<Texture, long>();
            var avatarMeshes = new HashSet<Mesh>();
            var avatarTextures = new HashSet<Texture>();
            foreach (var data in avatars)
            {
                if (data == null || !seenAvatars.Add(data)) continue;
                result.Avatars++;
                avatarMeshes.Clear();
                avatarTextures.Clear();
                var renderers = data.GetRenderers();
                if (renderers != null)
                    foreach (var renderer in renderers)
                    {
                        if (renderer == null) continue;
                        result.Renderers++;
                        if (renderer.sharedMesh != null) avatarMeshes.Add(renderer.sharedMesh);
                    }
                if (data.generatedMaterials?.materials != null)
                    foreach (var material in data.generatedMaterials.materials)
                    {
                        if (material?.resultingAtlasList == null) continue;
                        foreach (var texture in material.resultingAtlasList)
                            if (texture != null) avatarTextures.Add(texture);
                    }

                foreach (var mesh in avatarMeshes)
                {
                    if (!meshes.TryGetValue(mesh, out long bytes))
                    {
                        bytes = Math.Max(0, measure(mesh));
                        meshes.Add(mesh, bytes);
                        result.MeshBytes += bytes;
                        if (bytes == 0) result.UnavailableMemorySizes++;
                    }
                    result.MeshUses++;
                    result.MeshBytesWithoutSharing += bytes;
                }
                foreach (var texture in avatarTextures)
                {
                    if (!textures.TryGetValue(texture, out long bytes))
                    {
                        bytes = Math.Max(0, measure(texture));
                        textures.Add(texture, bytes);
                        result.TextureBytes += bytes;
                        if (bytes == 0) result.UnavailableMemorySizes++;
                    }
                    result.TextureUses++;
                    result.TextureBytesWithoutSharing += bytes;
                }
            }
            result.UniqueMeshes = meshes.Count;
            result.UniqueTextures = textures.Count;
            return result;
        }
    }
}
