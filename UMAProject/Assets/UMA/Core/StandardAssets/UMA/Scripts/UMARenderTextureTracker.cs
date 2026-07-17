using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Identifies render textures created while building UMA atlases. The information is
    /// intentionally kept separate from the texture lifetime so editor diagnostics can
    /// identify orphaned textures without retaining their owners.
    /// </summary>
    public static class UMARenderTextureTracker
    {
        public struct Ownership
        {
            public string characterName;
            public int umaDataInstanceId;
            public int atlasIndex;
            public int channelIndex;
            public string materialName;
            public string channelName;
            public bool temporary;

            public string CharacterLabel
            {
                get { return string.Format("{0} (UMAData {1})", characterName, umaDataInstanceId); }
            }
        }

        private const string RenderTextureNamePrefix = "UMA RT | ";
        private static readonly Dictionary<int, Ownership> ownershipByTextureId =
            new Dictionary<int, Ownership>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            ownershipByTextureId.Clear();
        }

        /// <summary>
        /// Adds ownership information and a descriptive object name to a UMA atlas render texture.
        /// </summary>
        public static void Track(
            RenderTexture texture,
            UMAData umaData,
            int atlasIndex,
            int channelIndex,
            string materialName,
            string channelName,
            bool temporary)
        {
            if (texture == null)
            {
                return;
            }

            Ownership ownership = new Ownership
            {
                characterName = GetCharacterName(umaData),
                umaDataInstanceId = umaData != null ? umaData.GetInstanceID() : 0,
                atlasIndex = atlasIndex,
                channelIndex = channelIndex,
                materialName = string.IsNullOrEmpty(materialName) ? "Unknown Material" : materialName,
                channelName = string.IsNullOrEmpty(channelName) ? "Unknown Channel" : channelName,
                temporary = temporary
            };

            ownershipByTextureId[texture.GetInstanceID()] = ownership;
            texture.name = string.Format(
                "{0}{1} [{2}] | Atlas {3} | {4} | {5}{6}",
                RenderTextureNamePrefix,
                ownership.characterName,
                ownership.umaDataInstanceId,
                ownership.atlasIndex,
                ownership.materialName,
                ownership.channelName,
                temporary ? " | Temporary" : string.Empty);
        }

        public static bool TryGetOwnership(RenderTexture texture, out Ownership ownership)
        {
            if (texture != null && ownershipByTextureId.TryGetValue(texture.GetInstanceID(), out ownership))
            {
                return true;
            }

            ownership = default(Ownership);
            return texture != null && TryGetOwnershipFromName(texture.name, out ownership);
        }

        /// <summary>
        /// Removes a texture from the active ownership inventory before it is released or destroyed.
        /// </summary>
        public static void Untrack(RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            ownershipByTextureId.Remove(texture.GetInstanceID());
            if (!string.IsNullOrEmpty(texture.name) && texture.name.StartsWith(RenderTextureNamePrefix, StringComparison.Ordinal))
            {
                texture.name = "Released " + texture.name;
            }
        }

        public static void ReleaseTemporary(RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            Untrack(texture);
            RenderTexture.ReleaseTemporary(texture);
        }

        /// <summary>
        /// Finds active UMA-tagged atlas textures that are no longer referenced by a live
        /// UMAData, its generated materials/renderers, or an asynchronous GPU readback.
        /// The caller owns the returned list but not the textures in it.
        /// </summary>
        public static List<RenderTexture> FindOrphanedRenderTextures()
        {
            HashSet<int> referencedTextureIds = GetLiveCharacterRenderTextureIds();
            List<RenderTexture> orphanedTextures = new List<RenderTexture>();
            RenderTexture[] renderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>();
            for (int textureIndex = 0; textureIndex < renderTextures.Length; textureIndex++)
            {
                RenderTexture texture = renderTextures[textureIndex];
                if (texture == null || string.IsNullOrEmpty(texture.name) ||
                    !texture.name.StartsWith(RenderTextureNamePrefix, StringComparison.Ordinal) ||
                    referencedTextureIds.Contains(texture.GetInstanceID()) ||
                    ReferenceEquals(RenderTexture.active, texture))
                {
                    continue;
                }

                Ownership ownership;
                if (!TryGetOwnership(texture, out ownership) || !RenderTexToCPU.SafeToFree(texture))
                {
                    continue;
                }

                orphanedTextures.Add(texture);
            }

            return orphanedTextures;
        }

        /// <summary>
        /// Rebuilds ownership entries from live UMAData components. This makes the
        /// diagnostics useful after a managed-domain reload as well as during Play mode.
        /// </summary>
        public static void RefreshOwnersFromLiveUMAData()
        {
            UMAData[] umaDataComponents = Resources.FindObjectsOfTypeAll<UMAData>();
            for (int umaDataIndex = 0; umaDataIndex < umaDataComponents.Length; umaDataIndex++)
            {
                UMAData umaData = umaDataComponents[umaDataIndex];
                if (umaData == null || umaData.generatedMaterials == null || umaData.generatedMaterials.materials == null)
                {
                    continue;
                }

                for (int atlasIndex = 0; atlasIndex < umaData.generatedMaterials.materials.Count; atlasIndex++)
                {
                    UMAData.GeneratedMaterial generatedMaterial = umaData.generatedMaterials.materials[atlasIndex];
                    if (generatedMaterial == null || generatedMaterial.resultingAtlasList == null)
                    {
                        continue;
                    }

                    string materialName = generatedMaterial.material != null
                        ? generatedMaterial.material.name
                        : generatedMaterial.umaMaterial != null ? generatedMaterial.umaMaterial.name : "Unknown Material";

                    for (int channelIndex = 0; channelIndex < generatedMaterial.resultingAtlasList.Length; channelIndex++)
                    {
                        RenderTexture texture = generatedMaterial.resultingAtlasList[channelIndex] as RenderTexture;
                        if (texture == null)
                        {
                            continue;
                        }

                        string channelName = generatedMaterial.textureNameList != null &&
                                             channelIndex < generatedMaterial.textureNameList.Length
                            ? generatedMaterial.textureNameList[channelIndex]
                            : string.Format("Channel {0}", channelIndex);
                        Track(texture, umaData, atlasIndex, channelIndex, materialName, channelName, false);
                    }
                }
            }
        }

        private static string GetCharacterName(UMAData umaData)
        {
            if (umaData == null)
            {
                return "Unknown Character";
            }

            return umaData.gameObject != null ? umaData.gameObject.name : umaData.name;
        }

        private static HashSet<int> GetLiveCharacterRenderTextureIds()
        {
            HashSet<int> textureIds = new HashSet<int>();
            UMAData[] umaDataComponents = Resources.FindObjectsOfTypeAll<UMAData>();
            for (int umaDataIndex = 0; umaDataIndex < umaDataComponents.Length; umaDataIndex++)
            {
                UMAData umaData = umaDataComponents[umaDataIndex];
                if (umaData == null)
                {
                    continue;
                }

                if (umaData.generatedMaterials != null && umaData.generatedMaterials.materials != null)
                {
                    for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
                    {
                        UMAData.GeneratedMaterial generatedMaterial = umaData.generatedMaterials.materials[materialIndex];
                        if (generatedMaterial == null)
                        {
                            continue;
                        }

                        AddRenderTextures(generatedMaterial.resultingAtlasList, textureIds);
                        AddMaterialRenderTextures(generatedMaterial.material, textureIds);
                        AddMaterialRenderTextures(generatedMaterial.secondPassMaterial, textureIds);
                    }
                }

                for (int rendererIndex = 0; rendererIndex < umaData.RendererCount; rendererIndex++)
                {
                    SkinnedMeshRenderer renderer = umaData.GetRenderer(rendererIndex);
                    if (renderer == null)
                    {
                        continue;
                    }

                    Material[] materials = renderer.sharedMaterials;
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        AddMaterialRenderTextures(materials[materialIndex], textureIds);
                    }
                }
            }

            return textureIds;
        }

        private static void AddRenderTextures(Texture[] textures, HashSet<int> textureIds)
        {
            if (textures == null)
            {
                return;
            }

            for (int textureIndex = 0; textureIndex < textures.Length; textureIndex++)
            {
                RenderTexture renderTexture = textures[textureIndex] as RenderTexture;
                if (renderTexture != null)
                {
                    textureIds.Add(renderTexture.GetInstanceID());
                }
            }
        }

        private static void AddMaterialRenderTextures(Material material, HashSet<int> textureIds)
        {
            if (material == null)
            {
                return;
            }

            string[] texturePropertyNames = material.GetTexturePropertyNames();
            for (int propertyIndex = 0; propertyIndex < texturePropertyNames.Length; propertyIndex++)
            {
                RenderTexture renderTexture = material.GetTexture(texturePropertyNames[propertyIndex]) as RenderTexture;
                if (renderTexture != null)
                {
                    textureIds.Add(renderTexture.GetInstanceID());
                }
            }
        }

        private static bool TryGetOwnershipFromName(string textureName, out Ownership ownership)
        {
            ownership = default(Ownership);
            if (string.IsNullOrEmpty(textureName) || !textureName.StartsWith(RenderTextureNamePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            const string atlasPrefix = " | Atlas ";
            int atlasPrefixIndex = textureName.IndexOf(atlasPrefix, RenderTextureNamePrefix.Length, StringComparison.Ordinal);
            if (atlasPrefixIndex < 0)
            {
                return false;
            }

            string characterAndId = textureName.Substring(RenderTextureNamePrefix.Length, atlasPrefixIndex - RenderTextureNamePrefix.Length);
            int idStart = characterAndId.LastIndexOf(" [", StringComparison.Ordinal);
            int idEnd = characterAndId.LastIndexOf(']');
            if (idStart < 0 || idEnd <= idStart + 2)
            {
                return false;
            }

            int umaDataInstanceId;
            if (!int.TryParse(characterAndId.Substring(idStart + 2, idEnd - idStart - 2), out umaDataInstanceId))
            {
                return false;
            }

            int atlasValueStart = atlasPrefixIndex + atlasPrefix.Length;
            int materialSeparator = textureName.IndexOf(" | ", atlasValueStart, StringComparison.Ordinal);
            if (materialSeparator < 0)
            {
                return false;
            }

            int atlasIndex;
            if (!int.TryParse(textureName.Substring(atlasValueStart, materialSeparator - atlasValueStart), out atlasIndex))
            {
                return false;
            }

            int channelSeparator = textureName.IndexOf(" | ", materialSeparator + 3, StringComparison.Ordinal);
            string materialName = channelSeparator >= 0
                ? textureName.Substring(materialSeparator + 3, channelSeparator - materialSeparator - 3)
                : textureName.Substring(materialSeparator + 3);
            string channelName = channelSeparator >= 0
                ? textureName.Substring(channelSeparator + 3)
                : "Unknown Channel";
            bool temporary = channelName.EndsWith(" | Temporary", StringComparison.Ordinal);
            if (temporary)
            {
                channelName = channelName.Substring(0, channelName.Length - " | Temporary".Length);
            }

            ownership = new Ownership
            {
                characterName = characterAndId.Substring(0, idStart),
                umaDataInstanceId = umaDataInstanceId,
                atlasIndex = atlasIndex,
                channelIndex = -1,
                materialName = materialName,
                channelName = channelName,
                temporary = temporary
            };
            return true;
        }
    }
}
